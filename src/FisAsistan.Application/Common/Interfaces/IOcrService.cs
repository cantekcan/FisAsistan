namespace FisAsistan.Application.Common.Interfaces;

public class OcrWordResult
{
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
}

public class OcrResult
{
    public string RawText { get; set; } = string.Empty;
    public double AverageConfidence { get; set; }
    public List<OcrWordResult> Words { get; set; } = new();
}

/// <summary>
/// Yerel, ücretsiz OCR motoru soyutlaması. Hiçbir görsel veya veri harici bir
/// AI servisine gönderilmez — tüm işleme bu arayüzün arkasındaki uygulamada,
/// makine üzerinde (Tesseract) gerçekleşir.
/// </summary>
public interface IOcrService
{
    Task<OcrResult> ExtractTextAsync(Stream imageStream, CancellationToken ct = default);
}
