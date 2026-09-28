using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Infrastructure.Segmentation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using static FisAsistan.Tests.Segmentation.SyntheticImageFactory;

namespace FisAsistan.Tests.Segmentation;

/// <summary>
/// Segmentasyon algoritmasını SENTETİK görsellerle (bkz. SyntheticImageFactory) test eder —
/// elimizde gerçek çoklu-fiş fotoğrafı olmadığı için dürüstçe programatik şekiller kullanılır.
/// Pure geometri yardımcıları (IoU, köşe sıralama) ayrıca doğrudan (görsel üretmeden) test edilir.
/// </summary>
public class OpenCvReceiptSegmentationServiceTests
{
    private readonly OpenCvReceiptSegmentationService _sut = new(NullLogger<OpenCvReceiptSegmentationService>.Instance);

    [Fact]
    public async Task SegmentAsync_UniformBackgroundNoReceipts_ReturnsZeroRegions()
    {
        using var stream = CreateImage(800, 600, Array.Empty<RectSpec>());

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeFalse();
        result.Segments.Should().BeEmpty();
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SegmentAsync_SingleReceipt_DetectsOneRegion()
    {
        using var stream = CreateImage(800, 600, new[]
        {
            new RectSpec(400, 300, 250, 450, 0)
        });

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(1);
        result.Segments[0].Corners.Should().HaveCount(4);
    }

    [Fact]
    public async Task SegmentAsync_TwoReceipts_DetectsTwoRegions()
    {
        using var stream = CreateImage(1000, 600, new[]
        {
            new RectSpec(250, 300, 200, 400, 0),
            new RectSpec(750, 300, 200, 400, 0)
        });

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(2);
    }

    [Fact]
    public async Task SegmentAsync_FourReceipts_DetectsFourRegions()
    {
        using var stream = CreateImage(1200, 900, new[]
        {
            new RectSpec(200, 200, 220, 300, -5),
            new RectSpec(600, 220, 200, 320, 4),
            new RectSpec(1000, 200, 210, 300, -8),
            new RectSpec(300, 650, 220, 300, 6)
        });

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(4);
    }

    [Fact]
    public async Task SegmentAsync_TenReceipts_DetectsTenRegions()
    {
        // 5 sütun x 2 satır, hepsi birbirinden yeterince ayrık ızgara düzeni.
        var rects = new List<RectSpec>();
        for (var row = 0; row < 2; row++)
        {
            for (var col = 0; col < 5; col++)
            {
                rects.Add(new RectSpec(150 + col * 300, 250 + row * 500, 220, 400, (col % 2 == 0 ? 3 : -3)));
            }
        }

        using var stream = CreateImage(1650, 1100, rects);

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(10);
    }

    [Fact]
    public async Task SegmentAsync_TinySpeck_IsFilteredOutByMinArea()
    {
        using var stream = CreateImage(800, 600, new[]
        {
            new RectSpec(400, 300, 250, 450, 0), // gerçek fiş boyutunda
            new RectSpec(50, 50, 6, 6, 0)        // minik gürültü lekesi
        });

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(1, "minik leke minimum alan filtresiyle elenmeli");
    }

    [Fact]
    public async Task SegmentAsync_ReceiptFillingAlmostWholeFrame_IsDetectedAsOneReceipt()
    {
        // Yakından çekilmiş tek bir fiş kadrajın neredeyse tamamını kaplar — bu bir fiştir,
        // "arka plan" diye elenmemelidir (eski davranış 0 dönüyordu, bu yanlıştı).
        using var stream = CreateImage(800, 600, new[]
        {
            new RectSpec(400, 300, 780, 580, 0)
        });

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(1);
    }

    [Fact]
    public async Task SegmentAsync_BrightBlankAreaWithoutText_IsNotAReceipt()
    {
        // Metin içermeyen parlak bir dikdörtgen (ör. beyaz kağıt parçası, pencere) fiş sayılmamalı.
        using var canvas = new OpenCvSharp.Mat(new OpenCvSharp.Size(800, 600), OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(35, 35, 35));
        OpenCvSharp.Cv2.Rectangle(canvas, new OpenCvSharp.Rect(250, 100, 250, 400), new OpenCvSharp.Scalar(245, 245, 245), -1);
        OpenCvSharp.Cv2.ImEncode(".png", canvas, out var bytes);

        var result = await _sut.SegmentAsync(new MemoryStream(bytes));

        result.Segments.Should().BeEmpty();
    }

    [Fact]
    public async Task SegmentAsync_InternalBoxedTotalsArea_IsNotDetectedAsSecondReceipt()
    {
        // Kullanıcının bildirdiği hata #1: fişin içindeki çerçeveli TOPKDV/TOPLAM alanı ikinci fiş sanılıyordu.
        using var stream = CreateReceiptWithBoxedTotals(800, 900);

        var result = await _sut.SegmentAsync(stream);

        result.Segments.Should().HaveCount(1);
    }

    [Fact]
    public async Task SegmentAsync_TwoTouchingReceiptsWithoutGap_AreSplitIntoTwo()
    {
        // Kullanıcının bildirdiği hata #2: aralarında boşluk olmadan yan yana duran fişler tek blob oluyordu.
        using var stream = CreateImage(900, 700, new[]
        {
            new RectSpec(300, 350, 300, 600, 0),
            new RectSpec(600, 350, 300, 600, 0) // ilk fişin sağ kenarına tam yapışık
        });

        var result = await _sut.SegmentAsync(stream);

        result.Segments.Should().HaveCount(2);
    }

    [Fact]
    public async Task SegmentAsync_ThreeTouchingReceiptsWithoutGap_AreSplitIntoThree()
    {
        using var stream = CreateImage(1000, 700, new[]
        {
            new RectSpec(200, 350, 300, 600, 0),
            new RectSpec(500, 350, 300, 600, 0),
            new RectSpec(800, 350, 300, 600, 0)
        });

        var result = await _sut.SegmentAsync(stream);

        result.Segments.Should().HaveCount(3);
    }

    [Fact]
    public async Task SegmentAsync_RotatedReceipt_DetectsWithFourValidCorners()
    {
        using var stream = CreateImage(800, 600, new[]
        {
            new RectSpec(400, 300, 220, 420, 25) // belirgin açı
        });

        var result = await _sut.SegmentAsync(stream);

        result.Success.Should().BeTrue();
        result.Segments.Should().HaveCount(1);
        result.Segments[0].Corners.Should().HaveCount(4);
        // Alan, dikdörtgenin gerçek alanına (220*420≈92400) yakın olmalı (açı/piksel yuvarlama payı ile).
        result.Segments[0].AreaPixels.Should().BeGreaterThan(70000).And.BeLessThan(115000);
    }

    [Fact]
    public async Task SegmentAsync_TouchingOverlappingReceipts_DoesNotProduceMoreRegionsThanInputShapes()
    {
        // İki fiş üst üste/bitişik olduğunda klasik contour detection bunları TEK bir bağlı
        // bileşen olarak görür — bu, fizibilite analizinde belirtilen bilinen bir sınırlamadır.
        // Test, algoritmanın en azından ÇÖKMEDİĞİNİ ve girdi şekli sayısını AŞMADIĞINI doğrular.
        using var stream = CreateImage(800, 600, new[]
        {
            new RectSpec(380, 300, 200, 400, 0),
            new RectSpec(420, 300, 200, 400, 0) // ilkiyle ciddi şekilde örtüşüyor
        });

        var result = await _sut.SegmentAsync(stream);

        result.Segments.Count.Should().BeLessThanOrEqualTo(2);
        // Beklenen gerçekçi davranış: birleşik tek blob olarak 1 bölge bulunur.
        if (result.Success)
        {
            result.Segments.Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task CropRegionAsync_DetectedCorners_ProducesNonEmptyStraightenedImage()
    {
        using var stream = CreateImage(800, 600, new[]
        {
            new RectSpec(400, 300, 220, 420, 12)
        });

        var segmentation = await _sut.SegmentAsync(stream);
        segmentation.Segments.Should().HaveCount(1);

        using var freshStream = CreateImage(800, 600, new[]
        {
            new RectSpec(400, 300, 220, 420, 12)
        });

        var cropBytes = await _sut.CropRegionAsync(freshStream, segmentation.Segments[0].Corners);

        cropBytes.Should().NotBeEmpty();
        // PNG imzası
        cropBytes[0].Should().Be(0x89);
        cropBytes[1].Should().Be((byte)'P');
    }

    // ---- Saf geometri yardımcıları: görsel üretmeden doğrudan test edilir ----

    [Fact]
    public void IoU_IdenticalBoxes_ReturnsOne()
    {
        var box = (X: 0.0, Y: 0.0, W: 100.0, H: 100.0);

        OpenCvReceiptSegmentationService.IoU(box, box).Should().Be(1.0);
    }

    [Fact]
    public void IoU_NonOverlappingBoxes_ReturnsZero()
    {
        var a = (X: 0.0, Y: 0.0, W: 10.0, H: 10.0);
        var b = (X: 100.0, Y: 100.0, W: 10.0, H: 10.0);

        OpenCvReceiptSegmentationService.IoU(a, b).Should().Be(0.0);
    }

    [Fact]
    public void IoU_HalfOverlappingBoxes_ReturnsExpectedRatio()
    {
        var a = (X: 0.0, Y: 0.0, W: 10.0, H: 10.0);
        var b = (X: 5.0, Y: 0.0, W: 10.0, H: 10.0);

        // kesişim = 5*10=50, birleşim = 100+100-50=150, IoU = 50/150 ≈ 0.333
        OpenCvReceiptSegmentationService.IoU(a, b).Should().BeApproximately(0.333, 0.01);
    }

    [Fact]
    public void OrderCorners_ScrambledInput_ReturnsTopLeftTopRightBottomRightBottomLeftOrder()
    {
        var scrambled = new[]
        {
            new OpenCvSharp.Point2f(0, 100),   // sol-alt
            new OpenCvSharp.Point2f(0, 0),     // sol-üst
            new OpenCvSharp.Point2f(100, 100), // sağ-alt
            new OpenCvSharp.Point2f(100, 0)    // sağ-üst
        };

        var ordered = OpenCvReceiptSegmentationService.OrderCorners(scrambled);

        ordered[0].Should().Be(new OpenCvSharp.Point2f(0, 0));
        ordered[1].Should().Be(new OpenCvSharp.Point2f(100, 0));
        ordered[2].Should().Be(new OpenCvSharp.Point2f(100, 100));
        ordered[3].Should().Be(new OpenCvSharp.Point2f(0, 100));
    }
}
