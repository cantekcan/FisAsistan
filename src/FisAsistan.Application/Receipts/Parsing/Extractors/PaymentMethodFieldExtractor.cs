using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Ödeme yöntemini anahtar kelimelerle bulur: NAKİT, KREDİ KARTI, BANKA KARTI, VISA, MASTERCARD.</summary>
public class PaymentMethodFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.PaymentMethod;

    private static readonly (Regex Pattern, string Label)[] Patterns =
    {
        (new Regex(@"NAKİT|NAKIT|CASH", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Nakit"),
        (new Regex(@"KREDİ\s*KART|KREDI\s*KART|CREDIT\s*CARD", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Kredi Kartı"),
        (new Regex(@"BANKA\s*KART|DEBIT\s*CARD", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Banka Kartı"),
        (new Regex(@"\bVISA\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Kredi Kartı (Visa)"),
        (new Regex(@"MASTERCARD", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Kredi Kartı (Mastercard)"),
        (new Regex(@"\bKART\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Kart"),
    };

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var (pattern, label) in Patterns)
        {
            foreach (var line in context.Lines)
            {
                if (pattern.IsMatch(line))
                {
                    return ExtractionCandidate.Success(label, $"'{line}' satırında '{label}' ödeme yöntemi ifadesi tespit edildi.");
                }
            }
        }

        return ExtractionCandidate.NotFound("Metinde tanınan bir ödeme yöntemi ifadesi (Nakit/Kredi Kartı/Banka Kartı) bulunamadı.");
    }
}
