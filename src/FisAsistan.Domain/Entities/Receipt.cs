using FisAsistan.Domain.Enums;

namespace FisAsistan.Domain.Entities;

/// <summary>
/// Yüklenen bir fiş belgesini temsil eder. Ham OCR metni burada saklanır;
/// alan bazlı çıkarım sonuçları ve kullanıcı düzeltmeleri ReceiptField satırlarında tutulur.
/// </summary>
public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.Uploaded;

    /// <summary>OCR motorunun ürettiği ham, düzenlenmemiş metin.</summary>
    public string? RawOcrText { get; set; }

    /// <summary>Tesseract'ın kelime bazlı güven skorlarının ortalaması (0-100), varsa.</summary>
    public double? OcrAverageConfidence { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    public ICollection<ReceiptField> Fields { get; set; } = new List<ReceiptField>();
    public ICollection<ReceiptVatLine> VatLines { get; set; } = new List<ReceiptVatLine>();
    public ICollection<ReceiptValidationIssue> ValidationIssues { get; set; } = new List<ReceiptValidationIssue>();
}
