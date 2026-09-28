using System.Text.Json;
using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Application.Receipts.Dtos;
using FisAsistan.Domain.Enums;
using FisAsistan.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FisAsistan.Infrastructure.ReceiptBatches;

/// <summary>
/// Onaylanmış bir ReceiptBatch'in bekleyen bölgelerini işler: her bölgeyi orijinal fotoğraftan
/// kırpar (perspektif düzeltmeli), sonra mevcut tek-fiş pipeline'ından (ön işleme → OCR → parser)
/// geçirip bağımsız bir Receipt'e dönüştürür.
///
/// <para><b>Eşzamanlılık notu:</b> Entity Framework Core'un <c>DbContext</c>'i thread-safe DEĞİLDİR —
/// aynı örneği paralel görevler arasında paylaşmak veri bozulmasına veya çalışma zamanı hatasına
/// yol açar. Bu yüzden her bölge kendi DI scope'unda (dolayısıyla kendi DbContext örneğinde)
/// işlenir. Tesseract OCR tarafında ise her çağrı kendi <c>TesseractEngine</c> örneğini
/// oluşturduğundan (bkz. TesseractOcrService) paralel çalışmak güvenlidir — sorun yalnızca TEK bir
/// engine örneğinin thread'ler arasında paylaşılmasında olurdu, burada öyle bir paylaşım yok.
/// Yine de CPU/bellek tüketimini sınırlamak için eşzamanlılık <see cref="MaxConcurrency"/> ile
/// sınırlandırılır.</para>
/// </summary>
public class ReceiptBatchProcessingService : IReceiptBatchProcessingService
{
    private const int MaxConcurrency = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptBatchProcessingService> _logger;

    public ReceiptBatchProcessingService(IServiceScopeFactory scopeFactory, ILogger<ReceiptBatchProcessingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProcessBatchAsync(Guid batchId, Guid userId, CancellationToken ct = default)
    {
        List<ReceiptBatchRegionDto> regions;
        string originalStoragePath;
        string originalFileName;

        using (var readScope = _scopeFactory.CreateScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<FisAsistanDbContext>();
            var batch = await db.ReceiptBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.UserId == userId, ct);
            if (batch is null)
            {
                _logger.LogWarning("ProcessBatchAsync: batch {BatchId} bulunamadı (userId {UserId}).", batchId, userId);
                return;
            }

            var parsedRegions = string.IsNullOrWhiteSpace(batch.PendingRegionsJson)
                ? new List<ReceiptBatchRegionDto>()
                : JsonSerializer.Deserialize<List<ReceiptBatchRegionDto>>(batch.PendingRegionsJson) ?? new();

            if (parsedRegions.Count == 0)
            {
                batch.Status = ReceiptBatchStatus.Failed;
                batch.Message = "İşlenecek bölge bulunamadı.";
                await db.SaveChangesAsync(ct);
                return;
            }

            batch.Status = ReceiptBatchStatus.Processing;
            await db.SaveChangesAsync(ct);

            regions = parsedRegions;
            originalStoragePath = batch.OriginalStoragePath;
            originalFileName = batch.OriginalFileName;
        }

        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var failureCount = 0;

        var tasks = regions.Select(async region =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
                var segmentationService = scope.ServiceProvider.GetRequiredService<IReceiptSegmentationService>();
                var processingService = scope.ServiceProvider.GetRequiredService<IReceiptProcessingService>();

                await using var originalStream = await fileStorage.OpenReadAsync(originalStoragePath, ct);
                var cropBytes = await segmentationService.CropRegionAsync(originalStream, region.Corners, ct);
                var cornersJson = JsonSerializer.Serialize(region.Corners);

                using var cropStream = new MemoryStream(cropBytes);
                await processingService.UploadAndProcessAsync(
                    userId, cropStream, $"{originalFileName}_fis{region.Index + 1}.png", "image/png", cropBytes.Length,
                    batchId: batchId, sourceRegionJson: cornersJson, ct: ct);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failureCount);
                _logger.LogError(ex, "Batch {BatchId} bölge {Index} işlenirken hata oluştu.", batchId, region.Index);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        using var finalScope = _scopeFactory.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<FisAsistanDbContext>();
        var finalBatch = await finalDb.ReceiptBatches.FirstAsync(b => b.Id == batchId, ct);
        finalBatch.Status = ReceiptBatchStatus.Completed;
        finalBatch.ProcessedAtUtc = DateTime.UtcNow;
        finalBatch.PendingRegionsJson = null;
        finalBatch.Message = failureCount > 0
            ? $"{regions.Count} bölgeden {failureCount} tanesi işlenirken hata oluştu, kalanlar başarıyla oluşturuldu."
            : $"{regions.Count} fiş başarıyla işlendi.";
        await finalDb.SaveChangesAsync(ct);
    }
}
