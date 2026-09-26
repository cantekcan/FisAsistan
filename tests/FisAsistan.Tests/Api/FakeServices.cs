using FisAsistan.Application.Common.Interfaces;

namespace FisAsistan.Tests.Api;

/// <summary>
/// Gerçek Tesseract motorunu çağırmadan, sabit bir örnek fiş metni döndüren sahte OCR servisi.
/// Böylece API entegrasyon testleri tessdata dosyalarına veya native kütüphanelere ihtiyaç duymaz.
/// </summary>
public class FakeOcrService : IOcrService
{
    public const string SampleReceiptText = """
        MIGROS TICARET A.S.
        BARBAROS MAH. AHI EVRAN CAD. NO:1
        VKN: 1234567890
        TARIH: 15.03.2026 SAAT: 14:32
        FIS NO: 000123
        ARA TOPLAM 57,50
        KDV %1 KDV 0,50
        KDV %10 KDV 4,50
        GENEL TOPLAM 62,50
        NAKIT
        """;

    public Task<OcrResult> ExtractTextAsync(Stream imageStream, CancellationToken ct = default)
    {
        return Task.FromResult(new OcrResult
        {
            RawText = SampleReceiptText,
            AverageConfidence = 92.5,
            Words = new List<OcrWordResult>()
        });
    }
}

public class FakeImagePreprocessor : IImagePreprocessor
{
    public async Task<MemoryStream> PreprocessAsync(Stream imageStream, CancellationToken ct = default)
    {
        var output = new MemoryStream();
        await imageStream.CopyToAsync(output, ct);
        output.Position = 0;
        return output;
    }
}
