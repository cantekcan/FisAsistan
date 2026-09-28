using FisAsistan.Domain.Enums;

namespace FisAsistan.Domain.Entities;

/// <summary>
/// Kullanıcının tek bir fotoğrafta birden fazla fiş yüklediği durumu temsil eder
/// (ör. masaya yan yana dizilmiş 10 fiş). Orijinal fotoğraf burada saklanır;
/// segmentasyon algoritmasının önerdiği bölgeler kullanıcı onayına sunulur
/// (<see cref="PendingRegionsJson"/>), onaydan sonra her bölge bağımsız bir
/// <see cref="Receipt"/> kaydına (mevcut OCR/parser pipeline'ından geçerek) dönüşür.
/// </summary>
public class ReceiptBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string OriginalStoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }

    public ReceiptBatchStatus Status { get; set; } = ReceiptBatchStatus.Uploaded;

    /// <summary>
    /// Kullanıcı onayı bekleyen bölge listesinin JSON serileştirilmiş hali
    /// (bkz. Application katmanındaki ReceiptBatchRegionDto). Segmentasyon sonucu ilk
    /// burada tutulur; kullanıcı silme/ekleme/düzenleme yaptıkça bu alan güncellenir.
    /// "process" adımında bu liste okunup her biri bir Receipt'e dönüştürülür.
    /// </summary>
    public string? PendingRegionsJson { get; set; }

    public string? Message { get; set; }

    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();
}
