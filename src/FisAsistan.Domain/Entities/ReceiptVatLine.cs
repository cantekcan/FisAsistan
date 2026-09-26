using FisAsistan.Domain.Enums;

namespace FisAsistan.Domain.Entities;

/// <summary>
/// Türkiye'de fişlerde birden fazla KDV oranı (%1, %10, %20 vb.) görülebildiği için
/// her oran ayrı bir satır olarak tutulur.
/// </summary>
public class ReceiptVatLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public decimal RatePercent { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal VatAmount { get; set; }

    public ValueSource Source { get; set; } = ValueSource.Parser;
}
