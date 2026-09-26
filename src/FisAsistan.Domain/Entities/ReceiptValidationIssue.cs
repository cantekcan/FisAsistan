using FisAsistan.Domain.Enums;

namespace FisAsistan.Domain.Entities;

/// <summary>
/// Parser'ın tutarlılık kontrollerinde (ör. ara toplam + KDV != genel toplam,
/// VKN formatı geçersiz, tarih bulunamadı) ürettiği uyarı/hatalar.
/// </summary>
public class ReceiptValidationIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public ReceiptFieldName? FieldName { get; set; }
    public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
    public string Message { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsResolved { get; set; }
}
