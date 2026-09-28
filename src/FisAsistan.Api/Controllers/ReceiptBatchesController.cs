using System.Text.Json;
using FisAsistan.Application.Common;
using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Application.Receipts.Dtos;
using FisAsistan.Domain.Entities;
using FisAsistan.Domain.Enums;
using FisAsistan.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FisAsistan.Api.Controllers;

/// <summary>
/// Tek bir fotoğrafta birden fazla fiş (ör. masaya yan yana dizilmiş 10 fiş) yükleme akışı.
/// Mevcut tekil fiş yükleme akışından (ReceiptsController) tamamen bağımsız çalışır —
/// oraya hiçbir şekilde dokunmaz. Akış: yükle → OpenCV segmentasyonu (öneri) → kullanıcı
/// onayı/düzeltmesi → onaylanan bölgeler mevcut OCR/parser pipeline'ından geçer → N Receipt.
/// </summary>
[ApiController]
[Authorize]
[Route("api/receipt-batches")]
public class ReceiptBatchesController : ControllerBase
{
    private readonly FisAsistanDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IReceiptSegmentationService _segmentationService;
    private readonly IReceiptBatchProcessingService _batchProcessingService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ReceiptBatchesController> _logger;

    public ReceiptBatchesController(
        FisAsistanDbContext db,
        IFileStorageService fileStorage,
        IReceiptSegmentationService segmentationService,
        IReceiptBatchProcessingService batchProcessingService,
        ICurrentUserService currentUser,
        ILogger<ReceiptBatchesController> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _segmentationService = segmentationService;
        _batchProcessingService = batchProcessingService;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(ReceiptFileUploadPolicy.MaxFileSizeBytes)]
    public async Task<ActionResult<ReceiptBatchDetailDto>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null)
        {
            return BadRequest(new { message = "Yüklenecek bir fotoğraf seçmelisiniz." });
        }

        var validationError = ReceiptFileUploadPolicy.Validate(file.FileName, file.ContentType, file.Length);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        await using var uploadStream = file.OpenReadStream();
        using var buffered = new MemoryStream();
        await uploadStream.CopyToAsync(buffered, ct);

        buffered.Position = 0;
        var storagePath = await _fileStorage.SaveAsync(buffered, file.FileName, ct);

        buffered.Position = 0;
        var segmentation = await _segmentationService.SegmentAsync(buffered, ct);

        var batch = new ReceiptBatch
        {
            UserId = _currentUser.UserId,
            OriginalFileName = file.FileName,
            OriginalStoragePath = storagePath,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            ImageWidth = segmentation.OriginalImageWidth,
            ImageHeight = segmentation.OriginalImageHeight,
            Message = segmentation.Message
        };

        if (segmentation.Success && segmentation.Segments.Count > 0)
        {
            batch.Status = ReceiptBatchStatus.RegionsProposed;
            batch.PendingRegionsJson = JsonSerializer.Serialize(segmentation.Segments.Select(s => new ReceiptBatchRegionDto
            {
                Index = s.Index,
                Corners = s.Corners,
                IsUserModified = false
            }).ToList());
        }
        else
        {
            batch.Status = ReceiptBatchStatus.SegmentationFailed;
        }

        _db.ReceiptBatches.Add(batch);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Action = "ReceiptBatchUploaded",
            Details = $"{file.FileName} ({segmentation.Segments.Count} bölge önerildi)"
        });

        await _db.SaveChangesAsync(ct);

        return Ok(await BuildDetailDtoAsync(batch.Id, ct));
    }

    [HttpGet]
    public async Task<ActionResult<List<ReceiptBatchDetailDto>>> List(CancellationToken ct)
    {
        var batchIds = await _db.ReceiptBatches.AsNoTracking()
            .Where(b => b.UserId == _currentUser.UserId)
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => b.Id)
            .ToListAsync(ct);

        var results = new List<ReceiptBatchDetailDto>();
        foreach (var id in batchIds)
        {
            var dto = await BuildDetailDtoAsync(id, ct);
            if (dto is not null)
            {
                results.Add(dto);
            }
        }

        return Ok(results);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptBatchDetailDto>> GetById(Guid id, CancellationToken ct)
    {
        var dto = await BuildDetailDtoAsync(id, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPut("{id:guid}/regions")]
    public async Task<ActionResult<ReceiptBatchDetailDto>> UpdateRegions(Guid id, UpdateBatchRegionsRequest request, CancellationToken ct)
    {
        var batch = await _db.ReceiptBatches.FirstOrDefaultAsync(b => b.Id == id && b.UserId == _currentUser.UserId, ct);
        if (batch is null)
        {
            return NotFound();
        }

        if (batch.Status is ReceiptBatchStatus.Processing or ReceiptBatchStatus.Completed)
        {
            return BadRequest(new { message = "İşlenmiş veya işlenmekte olan bir toplu yüklemenin bölgeleri değiştirilemez." });
        }

        if (request.Regions.Count == 0)
        {
            return BadRequest(new { message = "En az bir bölge kalmalıdır. Toplu yüklemeyi tamamen iptal etmek için silin." });
        }

        foreach (var region in request.Regions)
        {
            if (region.Corners.Length != 4)
            {
                return BadRequest(new { message = $"Bölge {region.Index}: tam olarak 4 köşe noktası gereklidir." });
            }
        }

        // İndeksleri yeniden numaralandır (kullanıcı silme/ekleme yapmış olabilir).
        var reindexed = request.Regions
            .Select((r, i) => new ReceiptBatchRegionDto { Index = i, Corners = r.Corners, IsUserModified = true })
            .ToList();

        batch.PendingRegionsJson = JsonSerializer.Serialize(reindexed);
        batch.Status = ReceiptBatchStatus.RegionsProposed;
        batch.Message = $"{reindexed.Count} bölge (kullanıcı tarafından düzenlendi).";

        await _db.SaveChangesAsync(ct);

        return Ok(await BuildDetailDtoAsync(id, ct));
    }

    [HttpPost("{id:guid}/process")]
    public async Task<ActionResult<ReceiptBatchDetailDto>> Process(Guid id, CancellationToken ct)
    {
        var batch = await _db.ReceiptBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == _currentUser.UserId, ct);
        if (batch is null)
        {
            return NotFound();
        }

        if (batch.Status != ReceiptBatchStatus.RegionsProposed)
        {
            return BadRequest(new
            {
                message = "Yalnızca onay bekleyen bölgeleri olan bir toplu yükleme işlenebilir."
            });
        }

        await _batchProcessingService.ProcessBatchAsync(id, _currentUser.UserId, ct);

        _db.AuditLogs.Add(new AuditLog { UserId = _currentUser.UserId, Action = "ReceiptBatchProcessed", Details = id.ToString() });
        await _db.SaveChangesAsync(ct);

        return Ok(await BuildDetailDtoAsync(id, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var batch = await _db.ReceiptBatches.FirstOrDefaultAsync(b => b.Id == id && b.UserId == _currentUser.UserId, ct);
        if (batch is null)
        {
            return NotFound();
        }

        await _fileStorage.DeleteAsync(batch.OriginalStoragePath, ct);
        _db.ReceiptBatches.Remove(batch);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    /// <summary>Toplu yüklemenin orijinal (kırpılmamış) fotoğrafını döner — manuel bölge çizim ekranı için.</summary>
    [HttpGet("{id:guid}/original-image")]
    public async Task<IActionResult> GetOriginalImage(Guid id, CancellationToken ct)
    {
        var batch = await _db.ReceiptBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == _currentUser.UserId, ct);
        if (batch is null)
        {
            return NotFound();
        }

        var stream = await _fileStorage.OpenReadAsync(batch.OriginalStoragePath, ct);
        return File(stream, string.IsNullOrWhiteSpace(batch.ContentType) ? "application/octet-stream" : batch.ContentType);
    }

    /// <summary>Belirli bir bölgenin (index'e göre, güncel PendingRegionsJson listesinden) anlık kırpma önizlemesini döner.</summary>
    [HttpGet("{id:guid}/regions/{index:int}/preview")]
    public async Task<IActionResult> GetRegionPreview(Guid id, int index, CancellationToken ct)
    {
        var batch = await _db.ReceiptBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == _currentUser.UserId, ct);
        if (batch is null || string.IsNullOrWhiteSpace(batch.PendingRegionsJson))
        {
            return NotFound();
        }

        var regions = JsonSerializer.Deserialize<List<ReceiptBatchRegionDto>>(batch.PendingRegionsJson) ?? new();
        var region = regions.FirstOrDefault(r => r.Index == index);
        if (region is null)
        {
            return NotFound();
        }

        await using var originalStream = await _fileStorage.OpenReadAsync(batch.OriginalStoragePath, ct);
        var cropBytes = await _segmentationService.CropRegionAsync(originalStream, region.Corners, ct);

        return File(cropBytes, "image/png");
    }

    private async Task<ReceiptBatchDetailDto?> BuildDetailDtoAsync(Guid id, CancellationToken ct)
    {
        var batch = await _db.ReceiptBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == _currentUser.UserId, ct);
        if (batch is null)
        {
            return null;
        }

        var pendingRegions = string.IsNullOrWhiteSpace(batch.PendingRegionsJson)
            ? new List<ReceiptBatchRegionDto>()
            : JsonSerializer.Deserialize<List<ReceiptBatchRegionDto>>(batch.PendingRegionsJson) ?? new();

        var receipts = await _db.Receipts.AsNoTracking()
            .Where(r => r.BatchId == id)
            .OrderBy(r => r.UploadedAtUtc)
            .ToListAsync(ct);

        var receiptIds = receipts.Select(r => r.Id).ToList();
        var fields = await _db.ReceiptFields.AsNoTracking()
            .Where(f => receiptIds.Contains(f.ReceiptId) &&
                        (f.FieldName == ReceiptFieldName.MerchantName || f.FieldName == ReceiptFieldName.TotalAmount || f.FieldName == ReceiptFieldName.Currency))
            .ToListAsync(ct);

        var issueCounts = await _db.ReceiptValidationIssues.AsNoTracking()
            .Where(i => receiptIds.Contains(i.ReceiptId) && !i.IsResolved)
            .GroupBy(i => i.ReceiptId)
            .Select(g => new { ReceiptId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ReceiptBatchDetailDto
        {
            Id = batch.Id,
            OriginalFileName = batch.OriginalFileName,
            Status = batch.Status,
            CreatedAtUtc = batch.CreatedAtUtc,
            ProcessedAtUtc = batch.ProcessedAtUtc,
            Message = batch.Message,
            ImageWidth = batch.ImageWidth,
            ImageHeight = batch.ImageHeight,
            PendingRegions = pendingRegions,
            Receipts = receipts.Select(r => new ReceiptListItemDto
            {
                Id = r.Id,
                OriginalFileName = r.OriginalFileName,
                Status = r.Status,
                UploadedAtUtc = r.UploadedAtUtc,
                MerchantName = fields.FirstOrDefault(f => f.ReceiptId == r.Id && f.FieldName == ReceiptFieldName.MerchantName)?.CurrentValue,
                TotalAmount = fields.FirstOrDefault(f => f.ReceiptId == r.Id && f.FieldName == ReceiptFieldName.TotalAmount)?.CurrentValue,
                Currency = fields.FirstOrDefault(f => f.ReceiptId == r.Id && f.FieldName == ReceiptFieldName.Currency)?.CurrentValue,
                OpenIssueCount = issueCounts.FirstOrDefault(c => c.ReceiptId == r.Id)?.Count ?? 0
            }).ToList()
        };
    }
}
