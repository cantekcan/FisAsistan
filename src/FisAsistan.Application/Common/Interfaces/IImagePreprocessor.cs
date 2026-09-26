namespace FisAsistan.Application.Common.Interfaces;

/// <summary>
/// OCR doğruluğunu artırmak için görsel ön işleme: döndürme, yeniden boyutlandırma,
/// gri tonlama, kontrast artırma, gürültü azaltma.
/// </summary>
public interface IImagePreprocessor
{
    /// <summary>Ham görsel akışını alır, işlenmiş görseli PNG olarak döndürür.</summary>
    Task<MemoryStream> PreprocessAsync(Stream imageStream, CancellationToken ct = default);
}
