namespace FisAsistan.Application.Common.Interfaces;

public interface IFileStorageService
{
    /// <summary>Dosyayı diske kaydeder, göreli depolama yolunu döndürür.</summary>
    Task<string> SaveAsync(Stream content, string suggestedFileName, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);

    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}
