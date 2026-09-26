namespace FisAsistan.Application.Common.Interfaces;

public interface IReceiptProcessingService
{
    /// <summary>
    /// Fiş görselini kaydeder, ön işleme + OCR + kural tabanlı parser'ı çalıştırır,
    /// sonuçları veritabanına yazar ve oluşturulan fişin Id'sini döndürür.
    /// </summary>
    Task<Guid> UploadAndProcessAsync(Guid userId, Stream fileStream, string originalFileName, string contentType, long fileSizeBytes, CancellationToken ct = default);
}
