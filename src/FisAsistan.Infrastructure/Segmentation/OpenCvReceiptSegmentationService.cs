using FisAsistan.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace FisAsistan.Infrastructure.Segmentation;

/// <summary>
/// Tek bir fotoğrafta birden fazla fiş olabileceği varsayımıyla, tamamen klasik görüntü işleme
/// (OpenCV) yöntemleriyle fiş adaylarını tespit eder. Hiçbir AI/LLM/ML modeli veya bulut servisi
/// kullanılmaz — tüm işleme yerel makinede, açıklanabilir kurallarla gerçekleşir.
///
/// <para><b>Temel fikir:</b> Bir fiş = açık renkli kağıt ÜZERİNDE yatay metin satırları.
/// Pipeline:</para>
/// <code>
/// Görsel → ön işleme (gri, bulanıklaştırma)
///        → kağıt maskesi (parlak + düşük doygunluk) ve metin maskesi (black-hat, yalnızca
///          karakter boyutundaki bileşenler — uzun dikey/yatay çizgiler, kenarlar metin sayılmaz)
///        → ham adaylar (kağıt maskesinin DIŞ konturları, delikleri doldurulmuş)
///        → aday filtreleme (göreli alan, en-boy oranı, dikdörtgensellik)
///        → içerme (containment) + tekrar (IoU) filtreleme
///        → birleşik fiş tespiti + bölme (metin geçmeyen tam-yükseklik dikey boşluk,
///          kenar/parlaklık sinyalleriyle desteklenerek)
///        → son doğrulama (her parçada yeterli metin satırı)
///        → orijinal koordinatlara dönüş (perspektif düzeltme kırpma sırasında yapılır)
/// </code>
///
/// <para>Sonuç KESİN kabul edilmemeli — çağıran taraf (ReceiptBatch akışı) kullanıcı onayı
/// almadan bu adayları doğrudan OCR'a göndermemelidir.</para>
/// </summary>
public class OpenCvReceiptSegmentationService : IReceiptSegmentationService
{
    // ---------------------------------------------------------------------------------------
    // Ayarlanabilir parametreler. Değerler, repodaki gerçek fiş fotoğrafları
    // (tests/.../TestData/RealReceipts) ve sentetik testler üzerinde ölçülerek seçildi.
    // ---------------------------------------------------------------------------------------

    /// <summary>Tespit bu boyuta küçültülmüş görselde yapılır (hız). Kırpma her zaman orijinalden yapılır.</summary>
    private const int DetectionMaxDimension = 1200;

    /// <summary>Aday alanının görsel alanına oranı bundan küçükse gürültü sayılır. Bilinçli olarak
    /// düşük tutuldu: 10 fişlik bir fotoğrafta tek bir fiş görselin ~%4'ü kadar olabilir.</summary>
    private const double MinAreaRatio = 0.01;

    private const double MaxAspectRatio = 8.0;

    /// <summary>Kontur alanı / döndürülmüş sınırlayıcı dikdörtgen alanı. Kağıt dikdörtgendir;
    /// düzensiz lekeler (kumaş dokusu, gölge) bu ölçütü geçemez.</summary>
    private const double MinRectangularity = 0.5;

    /// <summary>Kağıt piksellerinin azami HSV doygunluğu — kahverengi kumaş gibi renkli zeminler kağıt sayılmaz.</summary>
    private const int PaperMaxSaturation = 60;

    private const double DuplicateIoUThreshold = 0.5;

    /// <summary>Küçük adayın alanının bu oranı büyük adayın içindeyse, küçük olan "iç bölge"dir
    /// (ör. fiş içindeki TOPKDV/TOPLAM kutusu) ve fiş olarak kabul edilmez.</summary>
    internal const double ContainmentThreshold = 0.8;

    /// <summary>Bir bölgenin fiş sayılması için içermesi gereken asgari metin satırı sayısı.</summary>
    private const int MinTextLinesPerReceipt = 3;

    /// <summary>Koridor kolonunu kesebilecek azami metin satırı sayısı (başıboş tek bir çizgiye tolerans).</summary>
    private const int MaxSeamCrossings = 1;

    /// <summary>Koridor kolonundaki metin piksellerinin azami oranı (tek bir çizgiden fazlasını dışlayan güvenlik sınırı).</summary>
    private const double SeamMaxTextFraction = 0.06;

    /// <summary>Bölme sonrası her parçanın genişliği en az yüksekliğinin bu oranı kadar olmalı —
    /// tek bir fişin fiyat kolonu gibi dar şeritlerin ayrı fiş sanılmasını engeller.</summary>
    private const double MinPieceWidthToHeight = 0.18;

    /// <summary>Bölme sonrası her parça, bölünen adaydaki toplam metnin en az bu payını taşımalı.</summary>
    private const double MinPieceTextShare = 0.12;

    private const int MaxSplitDepth = 4;

    /// <summary>Dikiş aramasında denenen azami eğim (derece). Aday düzleştirmesindeki küçük
    /// sapmaları ve hafif eğik çekilmiş fotoğrafları tolere eder.</summary>
    private const int SeamMaxSlantDeg = 8;

    /// <summary>Bir yatay bileşenin "metin satırı/kelimesi" sayılması için birleştirmesi gereken
    /// asgari karakter sayısı. Desenli zeminlerdeki tekil noktalar/lekeler böylece metin sayılmaz.</summary>
    private const int MinCharactersPerTextGroup = 3;

    private readonly ILogger<OpenCvReceiptSegmentationService> _logger;

    public OpenCvReceiptSegmentationService(ILogger<OpenCvReceiptSegmentationService> logger)
    {
        _logger = logger;
    }

    public Task<ReceiptSegmentationResult> SegmentAsync(Stream imageStream, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var memory = new MemoryStream();
            imageStream.CopyTo(memory);

            using var original = Cv2.ImDecode(memory.ToArray(), ImreadModes.Color);
            if (original.Empty())
            {
                return new ReceiptSegmentationResult { Success = false, Message = "Görsel okunamadı veya bozuk." };
            }

            var originalWidth = original.Width;
            var originalHeight = original.Height;
            var longestSide = Math.Max(originalWidth, originalHeight);
            var detectionScale = longestSide > DetectionMaxDimension ? (double)DetectionMaxDimension / longestSide : 1.0;

            using var resized = detectionScale < 1.0
                ? original.Resize(new Size((int)(originalWidth * detectionScale), (int)(originalHeight * detectionScale)))
                : original.Clone();

            using var context = AnalysisContext.Build(resized);
            var quads = DetectReceiptQuads(context, _logger);

            if (quads.Count == 0)
            {
                return new ReceiptSegmentationResult
                {
                    Success = false,
                    OriginalImageWidth = originalWidth,
                    OriginalImageHeight = originalHeight,
                    Message = "Fotoğrafta güvenilir şekilde ayırt edilebilen bir fiş bölgesi bulunamadı. " +
                              "Fişlerin net göründüğünden ve birbirinin üzerine binmediğinden emin olun."
                };
            }

            var scaleBack = 1.0 / detectionScale;
            var segments = quads.Select((q, i) => ToCandidateDto(q, i, scaleBack)).ToList();

            _logger.LogInformation("Segmentasyon {Count} bölge buldu (görsel {Width}x{Height}).",
                segments.Count, originalWidth, originalHeight);

            return new ReceiptSegmentationResult
            {
                Success = true,
                Segments = segments,
                OriginalImageWidth = originalWidth,
                OriginalImageHeight = originalHeight,
                Message = $"{segments.Count} bölge tespit edildi."
            };
        }, ct);
    }

    public Task<byte[]> CropRegionAsync(Stream originalImageStream, SegmentPointDto[] corners, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            if (corners.Length != 4)
            {
                throw new ArgumentException("Kırpma için tam olarak 4 köşe noktası gereklidir.", nameof(corners));
            }

            using var memory = new MemoryStream();
            originalImageStream.CopyTo(memory);

            using var original = Cv2.ImDecode(memory.ToArray(), ImreadModes.Color);
            if (original.Empty())
            {
                throw new InvalidOperationException("Orijinal görsel okunamadı veya bozuk.");
            }

            var ordered = OrderCorners(corners.Select(c => new Point2f((float)c.X, (float)c.Y)).ToArray());
            var (destWidth, destHeight) = QuadSize(ordered);

            using var transform = Cv2.GetPerspectiveTransform(ordered, UprightRect(destWidth, destHeight));
            using var warped = new Mat();
            Cv2.WarpPerspective(original, warped, transform, new Size(destWidth, destHeight));

            Cv2.ImEncode(".png", warped, out var pngBytes);
            return pngBytes;
        }, ct);
    }

    // =======================================================================================
    // Pipeline
    // =======================================================================================

    internal static List<Quad> DetectReceiptQuads(AnalysisContext ctx, ILogger? logger = null)
    {
        var imageArea = ctx.Width * (double)ctx.Height;

        // 1) Ham adaylar: kağıt maskesinin DIŞ konturları. RETR_EXTERNAL kullanıldığı için bir
        //    fişin İÇİNDEKİ bölgeler (TOPKDV kutusu, ürün tablosu, çizgiler) hiçbir zaman ayrı
        //    kontur olarak dönmez — iç bölgeler yapısal olarak aday olamaz.
        var raw = ctx.PaperContours
            .Select(c => new Quad(OrderCorners(Cv2.MinAreaRect(c).Points()), Cv2.ContourArea(c)))
            .ToList();

        // 2) Temel filtreler: göreli alan, en-boy oranı, dikdörtgensellik.
        var filtered = raw.Where(q => PassesGeometryFilters(q, imageArea)).ToList();

        // 3) İçerme + tekrar filtresi (ikinci güvenlik ağı).
        filtered = RemoveContainedAndDuplicates(filtered);

        // 4) Birleşik fiş tespiti ve bölme (yan yana yapışık fişler tek blob olabilir).
        var split = filtered.SelectMany(q => SplitMergedCandidate(ctx, q, 0)).ToList();

        // 5) Son doğrulama: her bölge gerçekten metin satırları içeren bir fiş olmalı.
        var validated = split.Where(q => CountTextLines(ctx, q) >= MinTextLinesPerReceipt).ToList();

        // 6) Bölme sonrası yeniden içerme/tekrar filtresi + kullanıcı için anlamlı sıralama.
        var final = RemoveContainedAndDuplicates(validated);

        logger?.LogDebug(
            "Segmentasyon aşamaları: ham={Raw}, filtre={Filtered}, bölme={Split}, doğrulama={Validated}, son={Final}",
            raw.Count, filtered.Count, split.Count, validated.Count, final.Count);

        return final
            .OrderBy(q => Math.Round(q.Center.Y / (ctx.Height * 0.25)))
            .ThenBy(q => q.Center.X)
            .ToList();
    }

    private static bool PassesGeometryFilters(Quad q, double imageArea)
    {
        var (w, h) = QuadSize(q.Corners);
        if (w < 1 || h < 1)
        {
            return false;
        }

        var areaRatio = q.ContourArea / imageArea;
        if (areaRatio < MinAreaRatio)
        {
            return false;
        }

        var aspect = Math.Max(w, h) / (double)Math.Min(w, h);
        if (aspect > MaxAspectRatio)
        {
            return false;
        }

        var rectangularity = q.ContourArea / (w * (double)h);
        return rectangularity >= MinRectangularity;
    }

    /// <summary>
    /// Bir adayı, tam yükseklik boyunca hiçbir metin satırının geçmediği dikey bir boşluktan
    /// ikiye böler (özyinelemeli). Yan yana yapışık iki fişin arasından metin satırı geçmez;
    /// tek bir fişin içindeki kolon boşluklarını (ürün adı / KDV / fiyat) ise başlık ve alt bilgi
    /// satırları mutlaka keser — bu yüzden bu sinyal parlaklıktan bağımsız ve güvenilirdir.
    /// Dar boşluklarda ek olarak kenar sürekliliği veya parlaklık düşüşü aranır.
    /// </summary>
    private static IEnumerable<Quad> SplitMergedCandidate(AnalysisContext ctx, Quad candidate, int depth)
    {
        if (depth >= MaxSplitDepth)
        {
            return new[] { candidate };
        }

        var (width, height) = QuadSize(candidate.Corners);
        if (width < 20 || height < 20)
        {
            return new[] { candidate };
        }

        using var toUpright = Cv2.GetPerspectiveTransform(candidate.Corners, UprightRect(width, height));
        using var fromUpright = Cv2.GetPerspectiveTransform(UprightRect(width, height), candidate.Corners);

        using var gray = new Mat();
        using var lines = new Mat();
        Cv2.WarpPerspective(ctx.Gray, gray, toUpright, new Size(width, height));
        Cv2.WarpPerspective(ctx.LineMask, lines, toUpright, new Size(width, height), InterpolationFlags.Nearest);

        var seam = FindVerticalSeam(gray, lines, ctx.CharSize);
        if (seam is null)
        {
            return new[] { candidate };
        }

        // Dikiş, düzleştirilmiş görüntüde hafif eğik olabilir: x(y) = X - tan(θ)·(y - H/2).
        var tan = Math.Tan(seam.Value.AngleDeg * Math.PI / 180.0);
        var xTop = (float)(seam.Value.X + tan * height / 2.0);
        var xBottom = (float)(seam.Value.X - tan * height / 2.0);

        var left = MapQuad(fromUpright, new[]
        {
            new Point2f(0, 0), new Point2f(xTop, 0), new Point2f(xBottom, height), new Point2f(0, height)
        });
        var right = MapQuad(fromUpright, new[]
        {
            new Point2f(xTop, 0), new Point2f(width, 0), new Point2f(width, height), new Point2f(xBottom, height)
        });

        return SplitMergedCandidate(ctx, left, depth + 1)
            .Concat(SplitMergedCandidate(ctx, right, depth + 1));
    }

    /// <summary>
    /// Düzleştirilmiş aday görüntüsünde iki fişi ayıran dikişi (seam) arar. Aday düzleştirmesi
    /// (minAreaRect) kağıt kenarları eksik/düzensiz olduğunda birkaç derece sapabildiği için dikiş
    /// yalnızca tam dikey değil, ±<see cref="SeamMaxSlantDeg"/>° eğimli çizgiler boyunca da aranır
    /// (görüntü yatayda "shear" edilerek). Güvenilir bir dikiş yoksa null döner.
    /// </summary>
    internal static (double X, double AngleDeg)? FindVerticalSeam(Mat gray, Mat lineMask, int charSize)
    {
        (double X, double AngleDeg, double Score)? best = null;

        for (var angle = -SeamMaxSlantDeg; angle <= SeamMaxSlantDeg; angle++)
        {
            var tan = Math.Tan(angle * Math.PI / 180.0);
            using var shear = new Mat(2, 3, MatType.CV_64FC1);
            shear.Set(0, 0, 1.0); shear.Set(0, 1, tan); shear.Set(0, 2, -tan * gray.Height / 2.0);
            shear.Set(1, 0, 0.0); shear.Set(1, 1, 1.0); shear.Set(1, 2, 0.0);

            using var shearedGray = new Mat();
            using var shearedLines = new Mat();
            Cv2.WarpAffine(gray, shearedGray, shear, gray.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);
            Cv2.WarpAffine(lineMask, shearedLines, shear, lineMask.Size(), InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));

            var candidate = FindSeamInUprightImage(shearedGray, shearedLines, charSize);
            if (candidate is not null && (best is null || candidate.Value.Score > best.Value.Score))
            {
                best = (candidate.Value.X, angle, candidate.Value.Score);
            }
        }

        return best is null ? null : (best.Value.X, best.Value.AngleDeg);
    }

    /// <summary>
    /// Tam dikey kolonlar boyunca dikiş arar. Zorunlu koşul: tüm yükseklik boyunca hiçbir metin
    /// satırının geçmediği bir bant, ve bandın iki yanında da gerçek birer fiş (yeterli genişlik ve
    /// metin payı). Destekleyici sinyaller: bandın genişliği, sürekli dikey kenar (fotoğraf birleşim
    /// çizgisi / kağıt kenarı), parlaklık düşüşü (iki fiş arasında görünen zemin).
    /// </summary>
    private static (double X, double Score)? FindSeamInUprightImage(Mat gray, Mat lineMask, int charSize)
    {
        var width = gray.Width;
        var height = gray.Height;

        var textPerColumn = ColumnFractions(lineMask);
        var crossings = ColumnCrossings(lineMask);

        // Bir kolon "koridor" sayılır: en fazla MaxSeamCrossings metin satırı kesiyor VE toplam
        // metin oranı küçük. Tek bir başıboş çizginin (döndürme/sıkıştırma artefaktı) gerçek bir
        // dikişi gizlemesini önler; tek fişin iç kolon boşluklarını ise başlık/alt bilgi satırları ve
        // tam genişlikteki kesik çizgiler (-----) en az birkaç kez keser.
        bool IsCorridor(int x) => crossings[x] <= MaxSeamCrossings && textPerColumn[x] <= SeamMaxTextFraction;

        var totalText = textPerColumn.Sum();
        if (totalText <= 0)
        {
            return null;
        }

        using var sobel = new Mat();
        Cv2.Sobel(gray, sobel, MatType.CV_16S, 1, 0, 3);
        using var absSobel = new Mat();
        Cv2.ConvertScaleAbs(sobel, absSobel);
        using var strongEdges = new Mat();
        Cv2.Threshold(absSobel, strongEdges, 40, 255, ThresholdTypes.Binary);
        var edgePerColumn = ColumnFractions(strongEdges);

        using var columnMeans = new Mat();
        Cv2.Reduce(gray, columnMeans, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_64FC1.Value);
        columnMeans.GetArray(out double[] brightness);

        var minPieceWidth = Math.Max(height * MinPieceWidthToHeight, width * 0.12);
        var minGapWidth = Math.Max(4, (int)(width * 0.01));

        (double X, double Score)? best = null;

        var x0 = 0;
        while (x0 < width)
        {
            if (!IsCorridor(x0))
            {
                x0++;
                continue;
            }

            var x1 = x0;
            while (x1 + 1 < width && IsCorridor(x1 + 1))
            {
                x1++;
            }

            var touchesBorder = x0 == 0 || x1 == width - 1;
            var gapWidth = x1 - x0 + 1;
            var mid = (x0 + x1) / 2.0;

            if (!touchesBorder && gapWidth >= minGapWidth && mid >= minPieceWidth && width - mid >= minPieceWidth)
            {
                var leftShare = textPerColumn.Take((int)mid).Sum() / totalText;
                var rightShare = 1.0 - leftShare;

                if (leftShare >= MinPieceTextShare && rightShare >= MinPieceTextShare)
                {
                    var edgeSupport = edgePerColumn.Skip(x0).Take(gapWidth).Max();
                    var dip = BrightnessDip(brightness, x0, x1);

                    // Geniş boşluk tek başına yeterli; dar boşlukta fiziksel bir sınır kanıtı aranır.
                    var wideGap = gapWidth >= Math.Max(width * 0.025, charSize * 1.2);
                    var supported = wideGap || edgeSupport >= 0.5 || dip >= 15;

                    if (supported)
                    {
                        var score = gapWidth / (double)width + edgeSupport * 0.05 + Math.Max(0, dip) / 1000.0;
                        if (best is null || score > best.Value.Score)
                        {
                            best = (mid, score);
                        }
                    }
                }
            }

            x0 = x1 + 1;
        }

        return best;
    }

    /// <summary>Her kolonu dikey yönde kaç ayrı metin satırının kestiği (0→dolu geçiş sayısı).</summary>
    private static int[] ColumnCrossings(Mat binaryMask)
    {
        var width = binaryMask.Width;
        var height = binaryMask.Height;
        binaryMask.GetArray(out byte[] data);

        var counts = new int[width];
        for (var x = 0; x < width; x++)
        {
            var inside = false;
            for (var y = 0; y < height; y++)
            {
                var on = data[(y * width) + x] != 0;
                if (on && !inside)
                {
                    counts[x]++;
                }

                inside = on;
            }
        }

        return counts;
    }

    /// <summary>Her kolonda maskenin dolu olduğu satır oranı (0-1) — tek bir Reduce çağrısıyla.</summary>
    private static double[] ColumnFractions(Mat binaryMask)
    {
        using var sums = new Mat();
        Cv2.Reduce(binaryMask, sums, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_64FC1.Value);
        sums.GetArray(out double[] values);
        var denominator = 255.0 * binaryMask.Height;
        return values.Select(v => v / denominator).ToArray();
    }

    /// <summary>Boşluk bandının ortalama parlaklığının, hemen solundaki ve sağındaki şeritlerin
    /// ortalamasından ne kadar düşük olduğu (gri seviye). Fişler arasında görünen zemini yakalar.</summary>
    private static double BrightnessDip(double[] columnBrightness, int x0, int x1)
    {
        var band = Math.Max(3, (x1 - x0 + 1) / 2);
        var leftStart = Math.Max(0, x0 - band);
        var rightEnd = Math.Min(columnBrightness.Length - 1, x1 + band);
        if (leftStart >= x0 || rightEnd <= x1)
        {
            return 0;
        }

        var gapMean = columnBrightness.Skip(x0).Take(x1 - x0 + 1).Average();
        var leftMean = columnBrightness.Skip(leftStart).Take(x0 - leftStart).Average();
        var rightMean = columnBrightness.Skip(x1 + 1).Take(rightEnd - x1).Average();
        return (leftMean + rightMean) / 2.0 - gapMean;
    }

    /// <summary>Bir dörtgen bölgedeki metin satırı sayısı (yatay uzun metin satırı bileşenleri).</summary>
    internal static int CountTextLines(AnalysisContext ctx, Quad q)
    {
        var (width, height) = QuadSize(q.Corners);
        if (width < 10 || height < 10)
        {
            return 0;
        }

        // Karakterler, bölgenin DİK görüntüsünde yeniden satırlara gruplanır: global LineMask
        // görüntü eksenine göre yatay birleştirme yaptığı için belirgin eğik (≈25°+) fişlerde
        // satırlar kısa parçalara bölünür; dik çerçevede gruplama dönüklükten bağımsızdır.
        using var toUpright = Cv2.GetPerspectiveTransform(q.Corners, UprightRect(width, height));
        using var uprightText = new Mat();
        Cv2.WarpPerspective(ctx.TextMask, uprightText, toUpright, new Size(width, height), InterpolationFlags.Nearest);
        using var joined = AnalysisContext.GroupIntoTextLines(uprightText, ctx.CharSize);

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var count = Cv2.ConnectedComponentsWithStats(joined, labels, stats, centroids);

        var minLineWidth = Math.Max(ctx.CharSize * 3, (int)(width * 0.15));
        var lines = 0;
        for (var i = 1; i < count; i++)
        {
            var w = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var h = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
            if (w >= minLineWidth && w >= h * 3)
            {
                lines++;
            }
        }

        return lines;
    }

    internal static List<Quad> RemoveContainedAndDuplicates(List<Quad> candidates)
    {
        var sorted = candidates.OrderByDescending(c => c.BoxArea).ToList();
        var accepted = new List<Quad>();

        foreach (var candidate in sorted)
        {
            var box = BoundingBoxOf(candidate.Corners);
            var rejected = accepted.Any(a =>
            {
                var other = BoundingBoxOf(a.Corners);
                return IoU(box, other) > DuplicateIoUThreshold || ContainmentRatio(box, other) >= ContainmentThreshold;
            });

            if (!rejected)
            {
                accepted.Add(candidate);
            }
        }

        return accepted;
    }

    // =======================================================================================
    // Yardımcılar
    // =======================================================================================

    private static Quad MapQuad(Mat fromUpright, Point2f[] uprightCorners)
    {
        var mapped = Cv2.PerspectiveTransform(uprightCorners, fromUpright);
        var ordered = OrderCorners(mapped);
        var (w, h) = QuadSize(ordered);
        return new Quad(ordered, w * (double)h);
    }

    private static ReceiptSegmentCandidate ToCandidateDto(Quad q, int index, double scaleBack)
    {
        var box = BoundingBoxOf(q.Corners);
        return new ReceiptSegmentCandidate
        {
            Index = index,
            Corners = q.Corners.Select(p => new SegmentPointDto(p.X * scaleBack, p.Y * scaleBack)).ToArray(),
            BoundingX = box.X * scaleBack,
            BoundingY = box.Y * scaleBack,
            BoundingWidth = box.W * scaleBack,
            BoundingHeight = box.H * scaleBack,
            AreaPixels = q.ContourArea * scaleBack * scaleBack
        };
    }

    private static Point2f[] UprightRect(int width, int height) => new[]
    {
        new Point2f(0, 0),
        new Point2f(width - 1, 0),
        new Point2f(width - 1, height - 1),
        new Point2f(0, height - 1)
    };

    private static (int Width, int Height) QuadSize(Point2f[] ordered)
    {
        var width = Math.Max(Distance(ordered[0], ordered[1]), Distance(ordered[3], ordered[2]));
        var height = Math.Max(Distance(ordered[0], ordered[3]), Distance(ordered[1], ordered[2]));
        return (Math.Max((int)Math.Round(width), 10), Math.Max((int)Math.Round(height), 10));
    }

    /// <summary>4 köşeyi tutarlı bir sıraya (sol-üst, sağ-üst, sağ-alt, sol-alt) dizer —
    /// perspective transform'un doğru çalışması için şarttır.</summary>
    internal static Point2f[] OrderCorners(Point2f[] pts)
    {
        var sums = pts.Select(p => p.X + p.Y).ToArray();
        var diffs = pts.Select(p => p.X - p.Y).ToArray();

        var topLeft = pts[Array.IndexOf(sums, sums.Min())];
        var bottomRight = pts[Array.IndexOf(sums, sums.Max())];
        var topRight = pts[Array.IndexOf(diffs, diffs.Max())];
        var bottomLeft = pts[Array.IndexOf(diffs, diffs.Min())];

        return new[] { topLeft, topRight, bottomRight, bottomLeft };
    }

    internal static (double X, double Y, double W, double H) BoundingBoxOf(Point2f[] pts)
    {
        var minX = pts.Min(p => p.X);
        var maxX = pts.Max(p => p.X);
        var minY = pts.Min(p => p.Y);
        var maxY = pts.Max(p => p.Y);
        return (minX, minY, maxX - minX, maxY - minY);
    }

    internal static double IoU((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b)
    {
        var interArea = IntersectionArea(a, b);
        var unionArea = (a.W * a.H) + (b.W * b.H) - interArea;
        return unionArea <= 0 ? 0 : interArea / unionArea;
    }

    /// <summary>a kutusunun alanının ne kadarının b kutusunun içinde kaldığı (0-1).</summary>
    internal static double ContainmentRatio((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b)
    {
        var areaA = a.W * a.H;
        return areaA <= 0 ? 0 : IntersectionArea(a, b) / areaA;
    }

    private static double IntersectionArea((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.W, b.X + b.W);
        var y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        return Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
    }

    private static double Distance(Point2f a, Point2f b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    internal sealed record Quad(Point2f[] Corners, double ContourArea)
    {
        public Point2f Center => new(Corners.Average(p => p.X), Corners.Average(p => p.Y));

        public double BoxArea
        {
            get
            {
                var box = BoundingBoxOf(Corners);
                return box.W * box.H;
            }
        }
    }

    /// <summary>
    /// Tespit çözünürlüğünde bir kez hesaplanan ara görüntüler: gri ton, kağıt maskesi (delikleri
    /// doldurulmuş), metin maskesi ve kağıt konturları. Tüm native OpenCV kaynakları Dispose edilir.
    /// </summary>
    internal sealed class AnalysisContext : IDisposable
    {
        public Mat Gray { get; }
        public Mat PaperFilled { get; }

        /// <summary>Karakter boyutundaki koyu bileşenler (ham metin).</summary>
        public Mat TextMask { get; }

        /// <summary>Yatayda birleşerek en az <see cref="MinCharactersPerTextGroup"/> karakterlik
        /// kelime/satır oluşturan metin bölgeleri. Dikiş arama ve satır sayma bu maskeyi kullanır.</summary>
        public Mat LineMask { get; }

        public Point[][] PaperContours { get; }
        public int CharSize { get; }
        public int Width => Gray.Width;
        public int Height => Gray.Height;

        private AnalysisContext(Mat gray, Mat paperFilled, Mat textMask, Mat lineMask, Point[][] paperContours, int charSize)
        {
            Gray = gray;
            PaperFilled = paperFilled;
            TextMask = textMask;
            LineMask = lineMask;
            PaperContours = paperContours;
            CharSize = charSize;
        }

        public static AnalysisContext Build(Mat bgr)
        {
            var maxDim = Math.Max(bgr.Width, bgr.Height);

            // Karakter boyutu tahmini (tespit çözünürlüğünde). Black-hat çekirdeği karakterden büyük olmalı.
            var charSize = Math.Max(9, maxDim / 80) | 1;

            var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            using var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

            // --- Kağıt maskesi: parlak (Otsu) VE düşük doygunluk (renkli kumaş/masa hariç) ---
            using var bright = new Mat();
            Cv2.Threshold(blurred, bright, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            using var hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            using var saturation = hsv.ExtractChannel(1);
            using var lowSat = new Mat();
            Cv2.Threshold(saturation, lowSat, PaperMaxSaturation, 255, ThresholdTypes.BinaryInv);

            using var paper = new Mat();
            Cv2.BitwiseAnd(bright, lowSat, paper);

            using var smallKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.MorphologyEx(paper, paper, MorphTypes.Open, smallKernel);
            Cv2.MorphologyEx(paper, paper, MorphTypes.Close, smallKernel);

            Cv2.FindContours(paper, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            // Dış konturları DOLU çiz: metin/çizgi kaynaklı iç delikler kağıda dahil olur.
            var paperFilled = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.DrawContours(paperFilled, contours, -1, Scalar.All(255), thickness: -1);

            // --- Metin maskesi: açık zemin üzerindeki koyu, İNCE yapılar (black-hat) ---
            using var hatKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(charSize, charSize));
            using var blackHat = new Mat();
            Cv2.MorphologyEx(gray, blackHat, MorphTypes.BlackHat, hatKernel);

            using var textRaw = new Mat();
            Cv2.Threshold(blackHat, textRaw, 30, 255, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(textRaw, paperFilled, textRaw);

            // Uzun, dikeye yakın çizgileri (iki fotoğrafın/kağıdın birleşim çizgisi, kağıt kenarı,
            // gölge) Hough dönüşümüyle bulup metinden çıkar. Bu çizgiler döndürme/JPEG sıkıştırma
            // sonrası küçük parçalara bölünebildiği için tek tek "karakter boyutunda" görünüp metin
            // sayılabiliyor ve iki fişi ayıran koridoru kapatıyordu. HoughLinesP parçaları birleştirir.
            using var lineExclusion = DetectLongVerticalLines(gray, charSize);
            using var notLines = new Mat();
            Cv2.BitwiseNot(lineExclusion, notLines);
            Cv2.BitwiseAnd(textRaw, notLines, textRaw);

            // Karakter boyutunu görüntüdeki gerçek karakter yüksekliklerinden tahmin et: yakından
            // çekilmiş bir fişte yazılar, görüntü boyutundan türetilen varsayılandan çok daha büyüktür.
            var estimatedCharHeight = EstimateCharacterHeight(textRaw, maxDim);
            var effectiveCharSize = Math.Max(charSize, (int)Math.Round(estimatedCharHeight * 1.1)) | 1;

            var textMask = KeepCharacterSizedComponents(textRaw, effectiveCharSize);
            var lineMask = GroupIntoTextLines(textMask, effectiveCharSize);

            return new AnalysisContext(gray, paperFilled, textMask, lineMask, contours, effectiveCharSize);
        }

        /// <summary>Karakter benzeri bileşenlerin (makul boyut ve en-boy) yükseklik medyanı; bulunamazsa 0.</summary>
        private static double EstimateCharacterHeight(Mat binary, int maxDim)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var count = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

            var heights = new List<int>();
            for (var i = 1; i < count; i++)
            {
                var w = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                var h = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                if (h >= 4 && h <= maxDim / 12 && w <= h * 3)
                {
                    heights.Add(h);
                }
            }

            if (heights.Count < 10)
            {
                return 0;
            }

            heights.Sort();
            return heights[heights.Count / 2];
        }

        /// <summary>Görsel yüksekliğinin en az dörtte biri uzunluğundaki, dikeyden en fazla ~20° sapan
        /// çizgilerin kalın (karakter genişliğine yakın) bir maskesini üretir.</summary>
        private static Mat DetectLongVerticalLines(Mat gray, int charSize)
        {
            var mask = new Mat(gray.Size(), MatType.CV_8UC1, Scalar.All(0));

            using var edges = new Mat();
            Cv2.Canny(gray, edges, 50, 150);

            var minLength = gray.Height * 0.25;
            var maxGap = Math.Max(3, charSize / 3);
            var segments = Cv2.HoughLinesP(edges, 1, Math.PI / 180, 50, minLength, maxGap);

            var maxSlant = Math.Tan(20 * Math.PI / 180.0);
            var thickness = Math.Max(3, charSize / 2);
            foreach (var s in segments)
            {
                var dx = Math.Abs(s.P2.X - s.P1.X);
                var dy = Math.Abs(s.P2.Y - s.P1.Y);
                if (dy > 0 && dx <= dy * maxSlant)
                {
                    Cv2.Line(mask, s.P1, s.P2, Scalar.All(255), thickness);
                }
            }

            return mask;
        }

        /// <summary>
        /// Karakterleri yatayda birleştirir ve yalnızca en az <see cref="MinCharactersPerTextGroup"/>
        /// karakter içeren grupları (kelime/satır) tutar. Gerçek metin satırları çok sayıda yakın
        /// karakterden oluşur; desenli kumaş/masa gibi zeminlerdeki noktalar ise tekil kalır.
        /// </summary>
        internal static Mat GroupIntoTextLines(Mat textMask, int charSize)
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(charSize, 1));
            using var dilated = new Mat();
            Cv2.Dilate(textMask, dilated, kernel);

            using var groupLabels = new Mat();
            using var groupStats = new Mat();
            using var groupCentroids = new Mat();
            var groupCount = Cv2.ConnectedComponentsWithStats(dilated, groupLabels, groupStats, groupCentroids);

            using var charLabels = new Mat();
            using var charStats = new Mat();
            using var charCentroids = new Mat();
            var charCount = Cv2.ConnectedComponentsWithStats(textMask, charLabels, charStats, charCentroids);

            // Her karakteri, ilk pikselinin düştüğü gruba say. Dilatasyon karakteri tamamen
            // kapsadığı için bir karakterin tüm pikselleri zaten aynı gruptadır.
            groupLabels.GetArray(out int[] groupData);
            charLabels.GetArray(out int[] charData);

            var charsPerGroup = new int[groupCount];
            var seenChar = new bool[charCount];
            for (var i = 0; i < charData.Length; i++)
            {
                var c = charData[i];
                if (c == 0 || seenChar[c])
                {
                    continue;
                }

                seenChar[c] = true;
                charsPerGroup[groupData[i]]++;
            }

            // Gerçek kelime/satırlar YATAY olarak uzundur; desen kümeleri (ör. çiçekli masa örtüsündeki
            // birbirine yakın yapraklar) ise yaklaşık kare şeklindedir.
            var isTextGroup = new bool[groupCount];
            for (var g = 1; g < groupCount; g++)
            {
                var w = groupStats.At<int>(g, (int)ConnectedComponentsTypes.Width);
                var h = groupStats.At<int>(g, (int)ConnectedComponentsTypes.Height);
                isTextGroup[g] = charsPerGroup[g] >= MinCharactersPerTextGroup && w >= h * 1.5;
            }

            var output = new byte[groupData.Length];
            for (var i = 0; i < groupData.Length; i++)
            {
                output[i] = isTextGroup[groupData[i]] ? (byte)255 : (byte)0;
            }

            var result = new Mat(textMask.Rows, textMask.Cols, MatType.CV_8UC1);
            result.SetArray(output);
            return result;
        }

        /// <summary>
        /// Yalnızca karakter/kelime boyutundaki bileşenleri metin olarak tutar. Uzun dikey çizgiler
        /// (iki fotoğrafın birleşim çizgisi, kağıt kenarı gölgesi) metin sayılmaz — aksi halde iki
        /// fiş arasındaki dikiş "metin var" gibi görünüp bölmeyi engellerdi.
        /// </summary>
        private static Mat KeepCharacterSizedComponents(Mat binary, int charSize)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var count = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

            var maxHeight = charSize * 3;
            var keep = new bool[count];
            for (var i = 1; i < count; i++)
            {
                var h = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                keep[i] = h <= maxHeight && area >= 3;
            }

            labels.GetArray(out int[] labelData);
            var output = new byte[labelData.Length];
            for (var i = 0; i < labelData.Length; i++)
            {
                output[i] = keep[labelData[i]] ? (byte)255 : (byte)0;
            }

            var result = new Mat(binary.Rows, binary.Cols, MatType.CV_8UC1);
            result.SetArray(output);
            return result;
        }

        public void Dispose()
        {
            Gray.Dispose();
            PaperFilled.Dispose();
            TextMask.Dispose();
            LineMask.Dispose();
        }
    }
}
