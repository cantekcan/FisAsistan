using FisAsistan.Domain.Enums;

namespace FisAsistan.Domain.Entities;

/// <summary>
/// Bir fişin tek bir alanı için hem parser'ın bulduğu ham değeri hem de
/// kullanıcının onayladığı/düzelttiği güncel değeri ayrı ayrı tutar.
/// Böylece "OCR ne buldu" ile "kullanıcı ne onayladı" her zaman ayırt edilebilir.
/// </summary>
public class ReceiptField
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }

    public ReceiptFieldName FieldName { get; set; }

    /// <summary>Kural tabanlı parser'ın bulduğu orijinal değer (değişmez, denetim için saklanır).</summary>
    public string? ParserValue { get; set; }

    /// <summary>Şu anda geçerli olan değer: başlangıçta ParserValue ile aynıdır, kullanıcı düzeltirse güncellenir.</summary>
    public string? CurrentValue { get; set; }

    /// <summary>Değerin en son kaynağı: Parser mı bıraktı, kullanıcı mı değiştirdi.</summary>
    public ValueSource Source { get; set; } = ValueSource.Parser;

    /// <summary>Parser'ın bu alanı neden/nasıl bulduğuna dair açıklanabilir not (güven skoru değil).</summary>
    public string? ParserExplanation { get; set; }

    /// <summary>Kullanıcı bu alanı gözden geçirip onayladı mı.</summary>
    public bool IsConfirmed { get; set; }

    public DateTime? LastModifiedAtUtc { get; set; }
}
