using OpenCvSharp;

namespace FisAsistan.Tests.Segmentation;

/// <summary>
/// Testler için SENTETİK (programatik olarak OpenCV ile çizilmiş) görseller üretir: koyu bir
/// "masa" zemini üzerine, üstünde gerçek yazı satırları bulunan beyaz "fiş"ler. Her fiş önce
/// dik olarak ayrı bir görüntüye çizilir, sonra istenen açıyla döndürülüp zemine yerleştirilir —
/// böylece yazı satırları da fişle birlikte döner (gerçek fotoğraftaki gibi).
///
/// Not: Bunlar gerçek fotoğraf değildir; gerçek fotoğraflarla yapılan testler için
/// bkz. RealReceiptSegmentationTests (TestData/RealReceipts).
/// </summary>
internal static class SyntheticImageFactory
{
    public record RectSpec(float CenterX, float CenterY, float Width, float Height, float AngleDeg);

    public static MemoryStream CreateImage(int width, int height, IEnumerable<RectSpec> rects, Scalar? background = null)
    {
        using var canvas = new Mat(new Size(width, height), MatType.CV_8UC3, background ?? new Scalar(35, 35, 35));

        foreach (var r in rects)
        {
            DrawReceipt(canvas, r);
        }

        Cv2.ImEncode(".png", canvas, out var bytes);
        return new MemoryStream(bytes);
    }

    /// <summary>İçinde kalın çerçeveli bir "TOPKDV / TOPLAM" kutusu bulunan tek bir fiş çizer
    /// (fişin iç bölgelerinin ayrı fiş sanılmaması testi için).</summary>
    public static MemoryStream CreateReceiptWithBoxedTotals(int width, int height)
    {
        using var canvas = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(35, 35, 35));
        var spec = new RectSpec(width / 2f, height / 2f, width * 0.45f, height * 0.85f, 0);
        DrawReceipt(canvas, spec);

        var left = (int)(spec.CenterX - spec.Width / 2 + 15);
        var right = (int)(spec.CenterX + spec.Width / 2 - 15);
        var top = (int)(spec.CenterY + spec.Height * 0.15);
        Cv2.Rectangle(canvas, new Rect(left, top, right - left, (int)(spec.Height * 0.2)), new Scalar(0, 0, 0), 3);
        Cv2.PutText(canvas, "TOPKDV   *1,96", new Point(left + 10, top + 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 0), 2);
        Cv2.PutText(canvas, "TOPLAM  *26,15", new Point(left + 10, top + 65), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 0), 2);

        Cv2.ImEncode(".png", canvas, out var bytes);
        return new MemoryStream(bytes);
    }

    private static void DrawReceipt(Mat canvas, RectSpec r)
    {
        var w = Math.Max(20, (int)r.Width);
        var h = Math.Max(20, (int)r.Height);

        using var receipt = new Mat(new Size(w, h), MatType.CV_8UC3, new Scalar(245, 245, 245));
        var fontScale = Math.Max(0.35, w / 480.0);
        var lineHeight = (int)(30 * fontScale) + 6;
        var margin = Math.Max(6, w / 14);

        string[] lines =
        {
            "MARKET TIC. A.S.", "TARIH 12.12.2019", "FIS NO 0298", "EKMEK   %01  *12,50",
            "SUT 1LT  %08  *45,00", "CIPS     %08   *3,25", "POSET    %18   *0,25", "TOPKDV        *1,96",
            "TOPLAM       *62,50", "KREDI KARTI  *62,50"
        };

        var y = margin + lineHeight;
        foreach (var line in lines)
        {
            if (y > h - margin)
            {
                break;
            }

            Cv2.PutText(receipt, line, new Point(margin, y), HersheyFonts.HersheySimplex, fontScale, new Scalar(20, 20, 20), 1, LineTypes.AntiAlias);
            y += lineHeight;
        }

        // Fişi istenen açıyla döndürüp tuval üzerinde merkezine yerleştir.
        using var rotation = Cv2.GetRotationMatrix2D(new Point2f(w / 2f, h / 2f), -r.AngleDeg, 1.0);
        rotation.Set(0, 2, rotation.At<double>(0, 2) + r.CenterX - w / 2.0);
        rotation.Set(1, 2, rotation.At<double>(1, 2) + r.CenterY - h / 2.0);

        using var warped = new Mat();
        using var mask = new Mat();
        using var solid = new Mat(new Size(w, h), MatType.CV_8UC1, Scalar.All(255));
        Cv2.WarpAffine(receipt, warped, rotation, canvas.Size());
        Cv2.WarpAffine(solid, mask, rotation, canvas.Size());
        warped.CopyTo(canvas, mask);
    }
}
