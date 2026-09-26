using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Domain.Entities;
using FisAsistan.Domain.Enums;
using FisAsistan.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FisAsistan.Infrastructure.Receipts;

/// <summary>
/// Fiş yükleme akışının orkestratörü: dosyayı kaydeder → görüntüyü ön işler →
/// Tesseract ile OCR yapar → kural tabanlı parser'ı çalıştırır → sonuçları
/// (ham metin, alan bazlı çıkarımlar, KDV satırları, tutarlılık uyarıları) veritabanına yazar.
/// Hiçbir aşamada harici bir AI servisine veri gönderilmez.
/// </summary>
public class ReceiptProcessingService : IReceiptProcessingService
{
    private readonly FisAsistanDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IImagePreprocessor _preprocessor;
    private readonly IOcrService _ocrService;
    private readonly ReceiptParserService _parser;
    private readonly ILogger<ReceiptProcessingService> _logger;

    public ReceiptProcessingService(
        FisAsistanDbContext db,
        IFileStorageService fileStorage,
        IImagePreprocessor preprocessor,
        IOcrService ocrService,
        ReceiptParserService parser,
        ILogger<ReceiptProcessingService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _preprocessor = preprocessor;
        _ocrService = ocrService;
        _parser = parser;
        _logger = logger;
    }

    public async Task<Guid> UploadAndProcessAsync(
        Guid userId, Stream fileStream, string originalFileName, string contentType, long fileSizeBytes,
        CancellationToken ct = default)
    {
        using var buffered = new MemoryStream();
        await fileStream.CopyToAsync(buffered, ct);
        buffered.Position = 0;

        var storagePath = await _fileStorage.SaveAsync(buffered, originalFileName, ct);

        var receipt = new Receipt
        {
            UserId = userId,
            OriginalFileName = originalFileName,
            StoredFileName = storagePath,
            StoragePath = storagePath,
            ContentType = contentType,
            FileSizeBytes = fileSizeBytes,
            Status = ReceiptStatus.Processing,
            UploadedAtUtc = DateTime.UtcNow
        };

        _db.Receipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        try
        {
            buffered.Position = 0;
            using var preprocessed = await _preprocessor.PreprocessAsync(buffered, ct);

            preprocessed.Position = 0;
            var ocrResult = await _ocrService.ExtractTextAsync(preprocessed, ct);

            receipt.RawOcrText = ocrResult.RawText;
            receipt.OcrAverageConfidence = ocrResult.AverageConfidence;

            var parseResult = _parser.Parse(ocrResult.RawText);

            foreach (var (fieldName, candidate) in parseResult.Fields)
            {
                _db.ReceiptFields.Add(new ReceiptField
                {
                    ReceiptId = receipt.Id,
                    FieldName = fieldName,
                    ParserValue = candidate.Value,
                    CurrentValue = candidate.Value,
                    Source = ValueSource.Parser,
                    ParserExplanation = candidate.Explanation,
                    IsConfirmed = false
                });
            }

            foreach (var vat in parseResult.VatLines)
            {
                _db.ReceiptVatLines.Add(new ReceiptVatLine
                {
                    ReceiptId = receipt.Id,
                    RatePercent = vat.RatePercent,
                    BaseAmount = vat.BaseAmount,
                    VatAmount = vat.VatAmount,
                    Source = ValueSource.Parser
                });
            }

            foreach (var issue in parseResult.Issues)
            {
                _db.ReceiptValidationIssues.Add(new ReceiptValidationIssue
                {
                    ReceiptId = receipt.Id,
                    FieldName = issue.FieldName,
                    Severity = issue.Severity,
                    RuleCode = issue.RuleCode,
                    Message = issue.Message
                });
            }

            receipt.Status = ReceiptStatus.PendingReview;
            receipt.ProcessedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fiş {ReceiptId} işlenirken hata oluştu.", receipt.Id);
            receipt.Status = ReceiptStatus.Failed;
            _db.ReceiptValidationIssues.Add(new ReceiptValidationIssue
            {
                ReceiptId = receipt.Id,
                Severity = IssueSeverity.Error,
                RuleCode = "PROCESSING_FAILED",
                Message = $"OCR/parser işleme sırasında beklenmeyen hata: {ex.Message}"
            });
        }

        await _db.SaveChangesAsync(ct);
        return receipt.Id;
    }
}
