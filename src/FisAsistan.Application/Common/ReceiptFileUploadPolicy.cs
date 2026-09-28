namespace FisAsistan.Application.Common;

/// <summary>
/// Fiş görseli yüklemeleri için ortak güvenlik/doğrulama kuralları — hem tekil fiş yüklemesinde
/// (ReceiptsController) hem çoklu fiş (fotoğraf başına birden fazla fiş) yüklemesinde
/// (ReceiptBatchesController) aynı kurallar uygulanır.
/// </summary>
public static class ReceiptFileUploadPolicy
{
    public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };
    public static readonly string[] AllowedContentTypes = { "image/jpeg", "image/png", "image/bmp" };
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>Dosyayı doğrular; geçersizse kullanıcıya gösterilecek Türkçe hata mesajını döner, geçerliyse null.</summary>
    public static string? Validate(string fileName, string contentType, long length)
    {
        if (length == 0)
        {
            return "Yüklenecek bir dosya seçmelisiniz.";
        }

        if (length > MaxFileSizeBytes)
        {
            return $"Dosya boyutu {MaxFileSizeBytes / 1024 / 1024} MB sınırını aşıyor.";
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) || !AllowedContentTypes.Contains(contentType.ToLowerInvariant()))
        {
            return "Desteklenmeyen dosya türü. Yalnızca JPG, JPEG, PNG veya BMP dosyaları kabul edilir.";
        }

        return null;
    }
}
