using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Para birimini sembol/kod bazlı arar: ₺, TL, TRY, $, USD, €, EUR.</summary>
public class CurrencyFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.Currency;

    private static readonly (Regex Pattern, string Code)[] Patterns =
    {
        (new Regex(@"₺|\bTRY\b|\bTL\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "TRY"),
        // "$" tek başına aranmaz: OCR, "Ş" (örn. "A.Ş." unvanında) veya "5" gibi karakterleri
        // sıklıkla "$" olarak okuyabiliyor. Gerçek bir para birimi işareti genelde bir sayıya
        // bitişiktir (₺12,50, $12.50 gibi), bu yüzden yalnızca sayıya bitişik "$" kabul edilir.
        (new Regex(@"\$\s?\d|\d\s?\$|\bUSD\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "USD"),
        (new Regex(@"€|\bEUR\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "EUR"),
    };

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var (pattern, code) in Patterns)
        {
            foreach (var line in context.Lines)
            {
                if (pattern.IsMatch(line))
                {
                    return ExtractionCandidate.Success(code, $"'{line}' satırında '{code}' para birimi işareti tespit edildi.");
                }
            }
        }

        // Türkiye'deki fişlerin ezici çoğunluğu TL olduğundan, hiçbir sembol/kod bulunamazsa
        // varsayılan olarak TRY atanır. Bu bir OCR bulgusu değil bir varsayımdır — bu yüzden
        // açıklamada net şekilde belirtilir ve kullanıcı yine de gözden geçirip onaylamalıdır.
        return ExtractionCandidate.Success("TRY", "Para birimi sembolü/kodu bulunamadı; Türkiye'de fişlerin büyük çoğunluğu TL olduğundan varsayılan olarak TRY atandı, lütfen doğrulayın.");
    }
}
