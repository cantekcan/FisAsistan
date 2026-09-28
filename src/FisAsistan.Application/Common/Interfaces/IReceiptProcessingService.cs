namespace FisAsistan.Application.Common.Interfaces;

public interface IReceiptProcessingService
{
    /// <summary>
    /// Fiş görselini kaydeder, ön işleme + OCR + kural tabanlı parser'ı çalıştırır,
    /// sonuçları veritabanına yazar ve oluşturulan fişin Id'sini döndürür.
    ///
    /// <paramref name="batchId"/> ve <paramref name="sourceRegionJson"/>, çoklu fiş
    /// (bir fotoğrafta birden fazla fiş) akışından çağrıldığında doldurulur — tekil fiş
    /// yüklemesinde (mevcut davranış) ikisi de null kalır ve hiçbir fark oluşmaz.
    /// </summary>
    Task<Guid> UploadAndProcessAsync(
        Guid userId, Stream fileStream, string originalFileName, string contentType, long fileSizeBytes,
        Guid? batchId = null, string? sourceRegionJson = null, CancellationToken ct = default);
}
