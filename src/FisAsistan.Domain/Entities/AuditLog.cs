namespace FisAsistan.Domain.Entities;

/// <summary>
/// Kim, ne zaman, hangi fiş üzerinde ne yaptı — onay/red/düzenleme/silme gibi
/// önemli aksiyonların denetim kaydı.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid? ReceiptId { get; set; }

    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
