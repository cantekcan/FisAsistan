using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Application.Receipts.Dtos;
using FisAsistan.Domain.Entities;
using FisAsistan.Domain.Enums;
using FisAsistan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FisAsistan.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/receipts")]
public class ReceiptsController : ControllerBase
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
    private static readonly string[] AllowedContentTypes = { "image/jpeg", "image/png", "image/bmp" };
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly FisAsistanDbContext _db;
    private readonly IReceiptProcessingService _processingService;
    private readonly IReceiptExportService _exportService;
    private readonly IFileStorageService _fileStorage;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ReceiptsController> _logger;

    public ReceiptsController(
        FisAsistanDbContext db,
        IReceiptProcessingService processingService,
        IReceiptExportService exportService,
        IFileStorageService fileStorage,
        ICurrentUserService currentUser,
        ILogger<ReceiptsController> logger)
    {
        _db = db;
        _processingService = processingService;
        _exportService = exportService;
        _fileStorage = fileStorage;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes)]
    public async Task<ActionResult<ReceiptDetailDto>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "Yüklenecek bir dosya seçmelisiniz." });
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { message = $"Dosya boyutu {MaxFileSizeBytes / 1024 / 1024} MB sınırını aşıyor." });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || !AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new
            {
                message = "Desteklenmeyen dosya türü. Yalnızca JPG, JPEG, PNG veya BMP dosyaları kabul edilir."
            });
        }

        await using var stream = file.OpenReadStream();
        var receiptId = await _processingService.UploadAndProcessAsync(
            _currentUser.UserId, stream, file.FileName, file.ContentType, file.Length, ct);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            ReceiptId = receiptId,
            Action = "ReceiptUploaded",
            Details = file.FileName
        });
        await _db.SaveChangesAsync(ct);

        var dto = await BuildDetailDtoAsync(receiptId, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet]
    public async Task<ActionResult<List<ReceiptListItemDto>>> List(
        [FromQuery] ReceiptStatus? status, [FromQuery] string? search,
        [FromQuery] DateTime? dateFrom, [FromQuery] DateTime? dateTo, CancellationToken ct)
    {
        var query = _db.Receipts.AsNoTracking()
            .Where(r => r.UserId == _currentUser.UserId);

        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        if (dateFrom.HasValue)
        {
            query = query.Where(r => r.UploadedAtUtc >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            query = query.Where(r => r.UploadedAtUtc <= dateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(r => r.OriginalFileName.Contains(search) ||
                                      (r.RawOcrText != null && r.RawOcrText.Contains(search)));
        }

        var receipts = await query.OrderByDescending(r => r.UploadedAtUtc).ToListAsync(ct);
        var receiptIds = receipts.Select(r => r.Id).ToList();

        var fields = await _db.ReceiptFields
            .Where(f => receiptIds.Contains(f.ReceiptId))
            .Where(f => f.FieldName == ReceiptFieldName.MerchantName ||
                        f.FieldName == ReceiptFieldName.TotalAmount ||
                        f.FieldName == ReceiptFieldName.Currency)
            .ToListAsync(ct);

        var issueCounts = await _db.ReceiptValidationIssues
            .Where(i => receiptIds.Contains(i.ReceiptId) && !i.IsResolved)
            .GroupBy(i => i.ReceiptId)
            .Select(g => new { ReceiptId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var result = receipts.Select(r => new ReceiptListItemDto
        {
            Id = r.Id,
            OriginalFileName = r.OriginalFileName,
            Status = r.Status,
            UploadedAtUtc = r.UploadedAtUtc,
            MerchantName = fields.FirstOrDefault(f => f.ReceiptId == r.Id && f.FieldName == ReceiptFieldName.MerchantName)?.CurrentValue,
            TotalAmount = fields.FirstOrDefault(f => f.ReceiptId == r.Id && f.FieldName == ReceiptFieldName.TotalAmount)?.CurrentValue,
            Currency = fields.FirstOrDefault(f => f.ReceiptId == r.Id && f.FieldName == ReceiptFieldName.Currency)?.CurrentValue,
            OpenIssueCount = issueCounts.FirstOrDefault(c => c.ReceiptId == r.Id)?.Count ?? 0
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await BuildDetailDtoAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ReceiptDetailDto>> Update(Guid id, UpdateReceiptFieldsRequest request, CancellationToken ct)
    {
        var receipt = await _db.Receipts
            .Include(r => r.Fields)
            .Include(r => r.VatLines)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == _currentUser.UserId, ct);

        if (receipt is null)
        {
            return NotFound();
        }

        foreach (var item in request.Fields)
        {
            var field = receipt.Fields.FirstOrDefault(f => f.FieldName == item.FieldName);
            if (field is null)
            {
                continue;
            }

            var changed = field.CurrentValue != item.Value;
            field.CurrentValue = item.Value;
            field.IsConfirmed = item.IsConfirmed;
            if (changed)
            {
                field.Source = ValueSource.User;
                field.LastModifiedAtUtc = DateTime.UtcNow;
            }
        }

        foreach (var item in request.VatLines)
        {
            if (item.Id.HasValue)
            {
                var line = receipt.VatLines.FirstOrDefault(v => v.Id == item.Id.Value);
                if (line is not null)
                {
                    line.RatePercent = item.RatePercent;
                    line.BaseAmount = item.BaseAmount;
                    line.VatAmount = item.VatAmount;
                    line.Source = ValueSource.User;
                }
            }
            else
            {
                _db.ReceiptVatLines.Add(new ReceiptVatLine
                {
                    ReceiptId = receipt.Id,
                    RatePercent = item.RatePercent,
                    BaseAmount = item.BaseAmount,
                    VatAmount = item.VatAmount,
                    Source = ValueSource.User
                });
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            ReceiptId = receipt.Id,
            Action = "ReceiptFieldsUpdated"
        });

        await _db.SaveChangesAsync(ct);

        var dto = await BuildDetailDtoAsync(id, ct);
        return Ok(dto);
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<ReceiptDetailDto>> Approve(Guid id, CancellationToken ct)
    {
        var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == id && r.UserId == _currentUser.UserId, ct);
        if (receipt is null)
        {
            return NotFound();
        }

        var unresolvedErrors = await _db.ReceiptValidationIssues
            .Where(i => i.ReceiptId == id && !i.IsResolved && i.Severity == IssueSeverity.Error)
            .ToListAsync(ct);

        if (unresolvedErrors.Count > 0)
        {
            return BadRequest(new
            {
                message = "Kritik doğrulama hataları çözülmeden fiş onaylanamaz.",
                issues = unresolvedErrors.Select(i => i.Message)
            });
        }

        receipt.Status = ReceiptStatus.Approved;
        receipt.ReviewedByUserId = _currentUser.UserId;
        receipt.ReviewedAtUtc = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog { UserId = _currentUser.UserId, ReceiptId = id, Action = "ReceiptApproved" });
        await _db.SaveChangesAsync(ct);

        var dto = await BuildDetailDtoAsync(id, ct);
        return Ok(dto);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<ReceiptDetailDto>> Reject(Guid id, RejectReceiptRequest request, CancellationToken ct)
    {
        var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == id && r.UserId == _currentUser.UserId, ct);
        if (receipt is null)
        {
            return NotFound();
        }

        receipt.Status = ReceiptStatus.Rejected;
        receipt.ReviewedByUserId = _currentUser.UserId;
        receipt.ReviewedAtUtc = DateTime.UtcNow;
        receipt.RejectionReason = request.Reason;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            ReceiptId = id,
            Action = "ReceiptRejected",
            Details = request.Reason
        });
        await _db.SaveChangesAsync(ct);

        var dto = await BuildDetailDtoAsync(id, ct);
        return Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var receipt = await _db.Receipts.FirstOrDefaultAsync(r => r.Id == id && r.UserId == _currentUser.UserId, ct);
        if (receipt is null)
        {
            return NotFound();
        }

        await _fileStorage.DeleteAsync(receipt.StoragePath, ct);
        _db.Receipts.Remove(receipt);

        _db.AuditLogs.Add(new AuditLog { UserId = _currentUser.UserId, ReceiptId = id, Action = "ReceiptDeleted" });
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpGet("{id:guid}/image")]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var receipt = await _db.Receipts.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == _currentUser.UserId, ct);

        if (receipt is null)
        {
            return NotFound();
        }

        var stream = await _fileStorage.OpenReadAsync(receipt.StoragePath, ct);
        return File(stream, string.IsNullOrWhiteSpace(receipt.ContentType) ? "application/octet-stream" : receipt.ContentType);
    }

    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] ReceiptStatus? status, CancellationToken ct)
    {
        var rows = await BuildExportRowsAsync(status, ct);
        var bytes = _exportService.ExportToCsv(rows);
        return File(bytes, "text/csv", $"fisler_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
    }

    [HttpGet("export/xlsx")]
    public async Task<IActionResult> ExportXlsx([FromQuery] ReceiptStatus? status, CancellationToken ct)
    {
        var rows = await BuildExportRowsAsync(status, ct);
        var bytes = _exportService.ExportToXlsx(rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"fisler_{DateTime.UtcNow:yyyyMMdd_HHmm}.xlsx");
    }

    private async Task<List<ReceiptExportRowDto>> BuildExportRowsAsync(ReceiptStatus? status, CancellationToken ct)
    {
        var query = _db.Receipts.AsNoTracking().Where(r => r.UserId == _currentUser.UserId);
        if (status.HasValue)
        {
            query = query.Where(r => r.Status == status.Value);
        }

        var receipts = await query
            .Include(r => r.Fields)
            .Include(r => r.VatLines)
            .OrderByDescending(r => r.UploadedAtUtc)
            .ToListAsync(ct);

        return receipts.Select(r => new ReceiptExportRowDto
        {
            ReceiptId = r.Id.ToString(),
            MerchantName = Get(r, ReceiptFieldName.MerchantName),
            MerchantAddress = Get(r, ReceiptFieldName.MerchantAddress),
            TaxId = Get(r, ReceiptFieldName.TaxId),
            ReceiptDate = Get(r, ReceiptFieldName.ReceiptDate),
            ReceiptTime = Get(r, ReceiptFieldName.ReceiptTime),
            DocumentNumber = Get(r, ReceiptFieldName.DocumentNumber),
            SubTotal = Get(r, ReceiptFieldName.SubTotal),
            TotalVat = r.VatLines.Sum(v => v.VatAmount).ToString("0.00"),
            TotalAmount = Get(r, ReceiptFieldName.TotalAmount),
            Currency = Get(r, ReceiptFieldName.Currency),
            PaymentMethod = Get(r, ReceiptFieldName.PaymentMethod),
            Status = r.Status.ToString(),
            ApprovedAtUtc = r.ReviewedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty
        }).ToList();

        static string Get(Receipt r, ReceiptFieldName name) =>
            r.Fields.FirstOrDefault(f => f.FieldName == name)?.CurrentValue ?? string.Empty;
    }

    private async Task<ReceiptDetailDto?> BuildDetailDtoAsync(Guid id, CancellationToken ct)
    {
        var receipt = await _db.Receipts.AsNoTracking()
            .Include(r => r.Fields)
            .Include(r => r.VatLines)
            .Include(r => r.ValidationIssues)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == _currentUser.UserId, ct);

        if (receipt is null)
        {
            return null;
        }

        return new ReceiptDetailDto
        {
            Id = receipt.Id,
            OriginalFileName = receipt.OriginalFileName,
            ContentType = receipt.ContentType,
            FileSizeBytes = receipt.FileSizeBytes,
            Status = receipt.Status,
            UploadedAtUtc = receipt.UploadedAtUtc,
            ProcessedAtUtc = receipt.ProcessedAtUtc,
            RawOcrText = receipt.RawOcrText,
            OcrAverageConfidence = receipt.OcrAverageConfidence,
            RejectionReason = receipt.RejectionReason,
            Fields = receipt.Fields.Select(f => new ReceiptFieldDto
            {
                FieldName = f.FieldName,
                ParserValue = f.ParserValue,
                CurrentValue = f.CurrentValue,
                Source = f.Source,
                ParserExplanation = f.ParserExplanation,
                IsConfirmed = f.IsConfirmed
            }).ToList(),
            VatLines = receipt.VatLines.Select(v => new ReceiptVatLineDto
            {
                Id = v.Id,
                RatePercent = v.RatePercent,
                BaseAmount = v.BaseAmount,
                VatAmount = v.VatAmount,
                Source = v.Source
            }).ToList(),
            ValidationIssues = receipt.ValidationIssues.Select(i => new ReceiptValidationIssueDto
            {
                FieldName = i.FieldName,
                Severity = i.Severity,
                Message = i.Message,
                RuleCode = i.RuleCode,
                IsResolved = i.IsResolved
            }).ToList()
        };
    }
}
