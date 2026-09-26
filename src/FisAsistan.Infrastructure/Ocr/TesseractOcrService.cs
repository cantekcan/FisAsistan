using FisAsistan.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tesseract;

namespace FisAsistan.Infrastructure.Ocr;

/// <summary>
/// Yerel, tamamen ücretsiz ve çevrimdışı çalışan OCR uygulaması. Tesseract açık kaynak
/// motorunu kullanır; hiçbir görsel veya metin ağa/harici bir servise gönderilmez.
/// </summary>
public class TesseractOcrService : IOcrService
{
    private readonly TesseractOptions _options;
    private readonly ILogger<TesseractOcrService> _logger;

    public TesseractOcrService(IOptions<TesseractOptions> options, ILogger<TesseractOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<OcrResult> ExtractTextAsync(Stream imageStream, CancellationToken ct = default)
    {
        // Tesseract'ın .NET sarmalayıcısı senkron/native çağrılar kullanır; CPU'ya bağlı iş
        // olduğu için thread pool üzerinde çalıştırılır (asenkron API'ye zorla sarılır).
        return Task.Run(() =>
        {
            using var memory = new MemoryStream();
            imageStream.CopyTo(memory);
            memory.Position = 0;

            using var engine = new TesseractEngine(_options.TessDataPath, _options.Languages, EngineMode.Default);
            using var img = Pix.LoadFromMemory(memory.ToArray());
            // PSM.SingleBlock: fişler görsel olarak tek bir metin bloğu (satır satır, sağa
            // hizalı fiyat sütunuyla) olduğundan varsayılan "Auto" moddan daha güvenilir sonuç verir —
            // Auto modda sağdaki fiyat sütunu ayrı/gürültü olarak algılanıp atlanabiliyor.
            using var page = engine.Process(img, PageSegMode.SingleBlock);

            var text = page.GetText() ?? string.Empty;
            var words = new List<OcrWordResult>();

            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    var wordText = iter.GetText(PageIteratorLevel.Word);
                    if (!string.IsNullOrWhiteSpace(wordText))
                    {
                        var confidence = iter.GetConfidence(PageIteratorLevel.Word);
                        words.Add(new OcrWordResult { Text = wordText.Trim(), Confidence = confidence });
                    }
                } while (iter.Next(PageIteratorLevel.Word));
            }

            var avgConfidence = words.Count > 0 ? words.Average(w => w.Confidence) : 0d;

            _logger.LogInformation("Tesseract OCR tamamlandı. Kelime sayısı: {WordCount}, ortalama güven: {Confidence:F1}",
                words.Count, avgConfidence);

            return new OcrResult
            {
                RawText = text,
                AverageConfidence = avgConfidence,
                Words = words
            };
        }, ct);
    }
}
