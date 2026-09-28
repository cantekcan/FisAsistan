namespace FisAsistan.Application.Common.Interfaces;

/// <summary>İki boyutlu bir piksel koordinatı — orijinal (kırpılmamış) fotoğraf koordinat sisteminde.</summary>
public record SegmentPointDto(double X, double Y);

/// <summary>
/// Segmentasyon algoritmasının tespit ettiği tek bir fiş adayı. Görsel veri (byte[]) burada
/// TUTULMAZ — bellek/JSON boyutunu şişirmemek için; ilgili bölge, ihtiyaç duyulduğunda
/// (önizleme veya işleme sırasında) orijinal fotoğraftan bu köşe koordinatlarıyla yeniden
/// kırpılır (bkz. IReceiptSegmentationService.CropRegionAsync).
/// </summary>
public class ReceiptSegmentCandidate
{
    public int Index { get; set; }

    /// <summary>4 köşe noktası (sol-üst, sağ-üst, sağ-alt, sol-alt sırasıyla), orijinal fotoğraf koordinatlarında.</summary>
    public SegmentPointDto[] Corners { get; set; } = Array.Empty<SegmentPointDto>();

    public double BoundingX { get; set; }
    public double BoundingY { get; set; }
    public double BoundingWidth { get; set; }
    public double BoundingHeight { get; set; }

    public double AreaPixels { get; set; }
}

public class ReceiptSegmentationResult
{
    /// <summary>En az bir güvenilir bölge bulunduysa true. Bulunamazsa false — bu bir hata değil, "0 bölge" sonucudur.</summary>
    public bool Success { get; set; }

    public List<ReceiptSegmentCandidate> Segments { get; set; } = new();

    public string? Message { get; set; }

    public int OriginalImageWidth { get; set; }
    public int OriginalImageHeight { get; set; }
}

/// <summary>
/// Tek bir fotoğrafta birden fazla fiş olabileceği varsayımıyla, klasik görüntü işleme
/// yöntemleriyle (eşikleme, contour detection, minAreaRect, perspective correction —
/// OpenCV tabanlı) fiş adaylarını tespit eden servis. Hiçbir AI/LLM/bulut servisi kullanmaz;
/// tüm işleme yerel makinede gerçekleşir. Sonuç KESİN kabul edilmemelidir — kullanıcı
/// onayı/düzeltmesi olmadan doğrudan OCR'a gönderilmemelidir (bkz. ReceiptBatch akışı).
/// </summary>
public interface IReceiptSegmentationService
{
    Task<ReceiptSegmentationResult> SegmentAsync(Stream imageStream, CancellationToken ct = default);

    /// <summary>
    /// Verilen 4 köşe koordinatına göre orijinal fotoğraftan perspektif düzeltmeli bir kırpma
    /// üretir (PNG bayt dizisi). Hem otomatik tespit edilen hem kullanıcının elle çizdiği
    /// (eksen-hizalı) bölgeler için kullanılır.
    /// </summary>
    Task<byte[]> CropRegionAsync(Stream originalImageStream, SegmentPointDto[] corners, CancellationToken ct = default);
}
