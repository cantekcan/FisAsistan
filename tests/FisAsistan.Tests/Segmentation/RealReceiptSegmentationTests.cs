using FisAsistan.Infrastructure.Segmentation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FisAsistan.Tests.Segmentation;

/// <summary>
/// GERÇEK fiş fotoğraflarıyla (TestData/RealReceipts, kullanıcının sağladığı telefon çekimleri;
/// kişisel veri olduğundan repoya konmaz — klasör yoksa testler hiçbir şey doğrulamadan geçer)
/// segmentasyon regresyon testleri. Bildirilen iki hatayı kapsar:
///  #1 tek fişin iç bölgeleri (TOPKDV/TOPLAM kutusu) ikinci fiş sanılıyordu,
///  #2 birbirine yapışık fişler ([BİM][ŞOK][TURUNCU]) birleşik tek aday oluyordu.
/// </summary>
public class RealReceiptSegmentationTests
{
    private readonly OpenCvReceiptSegmentationService _sut = new(NullLogger<OpenCvReceiptSegmentationService>.Instance);

    [Theory]
    [InlineData("single-bim-on-fabric.jpg", 1)]
    [InlineData("single-sec-on-light-pattern.jpg", 1)]
    [InlineData("two-bim-turuncu-touching.jpg", 2)]
    [InlineData("three-bim-sok-turuncu-touching.jpg", 3)]
    public async Task SegmentAsync_RealPhoto_DetectsExpectedReceiptCount(string fileName, int expectedCount)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "RealReceipts", fileName);
        if (!File.Exists(path))
        {
            // Gerçek fotoğraflar kişisel veri içerdiği için repoya konmaz (.gitignore); yalnızca
            // fotoğrafların bulunduğu yerel makinede çalışır.
            return;
        }

        await using var stream = File.OpenRead(path);
        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(expectedCount);

        var imageArea = (double)result.OriginalImageWidth * result.OriginalImageHeight;
        foreach (var segment in result.Segments)
        {
            segment.Corners.Should().HaveCount(4);
            // Her bölge gerçek bir fiş boyutunda olmalı (iç kutu/parça değil).
            (segment.AreaPixels / imageArea).Should().BeGreaterThan(0.05);
        }

        // Bölünmüş fişler birbirinin üstüne binmemeli ve soldan sağa sıralı olmalı.
        var boxes = result.Segments
            .Select(s => (X: s.Corners.Min(c => c.X), Y: s.Corners.Min(c => c.Y),
                          W: s.Corners.Max(c => c.X) - s.Corners.Min(c => c.X),
                          H: s.Corners.Max(c => c.Y) - s.Corners.Min(c => c.Y)))
            .ToList();
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                OpenCvReceiptSegmentationService.IoU(boxes[i], boxes[j]).Should().BeLessThan(0.2);
            }
        }

        boxes.Select(b => b.X + b.W / 2).Should().BeInAscendingOrder();
    }
}
