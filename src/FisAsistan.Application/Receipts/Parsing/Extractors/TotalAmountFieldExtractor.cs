using System.Globalization;
using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// Genel toplamı bulur. Öncelik sırası: "GENEL TOPLAM" / "ÖDENECEK TUTAR" / "ÖDENEN TUTAR"
/// (en kesin ifadeler) → bulunamazsa "ARA TOPLAM" OLMAYAN "TOPLAM" satırı.
/// Birden fazla aday varsa fişteki en son (genelde en alttaki, yani nihai) TOPLAM satırı tercih edilir.
/// </summary>
public class TotalAmountFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.TotalAmount;

    private static readonly Regex PrimaryLabel = new(
        @"(GENEL\s*TOPLAM|ÖDENECEK\s*TUTAR|ODENECEK\s*TUTAR|ÖDENEN\s*TUTAR|TOPLAM\s*TUTAR)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FallbackLabel = new(
        @"(?<!ARA\s)(?<!ARA)TOPLAM", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        var primaryHits = new List<(string Line, string Raw, decimal Amount)>();
        foreach (var line in context.Lines)
        {
            var (found, raw, amount) = AmountLineHelper.TryFindAmountAfterLabel(line, PrimaryLabel);
            if (found && amount.HasValue)
            {
                primaryHits.Add((line, raw, amount.Value));
            }
        }

        if (primaryHits.Count > 0)
        {
            var pick = primaryHits[^1];
            return ExtractionCandidate.Success(
                pick.Amount.ToString(CultureInfo.InvariantCulture),
                $"'{pick.Line}' satırında 'GENEL TOPLAM/ÖDENECEK TUTAR' etiketiyle bulundu: '{pick.Raw}'.");
        }

        var fallbackHits = new List<(string Line, string Raw, decimal Amount)>();
        foreach (var line in context.Lines)
        {
            if (line.Contains("ARA TOPLAM", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ARATOPLAM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (found, raw, amount) = AmountLineHelper.TryFindAmountAfterLabel(line, FallbackLabel);
            if (found && amount.HasValue)
            {
                fallbackHits.Add((line, raw, amount.Value));
            }
        }

        if (fallbackHits.Count > 0)
        {
            var pick = fallbackHits[^1];
            return ExtractionCandidate.Success(
                pick.Amount.ToString(CultureInfo.InvariantCulture),
                $"'{pick.Line}' satırında 'TOPLAM' etiketiyle bulundu (ara toplam hariç): '{pick.Raw}'.");
        }

        return ExtractionCandidate.NotFound("Metinde 'GENEL TOPLAM', 'ÖDENECEK TUTAR' veya 'TOPLAM' etiketiyle bir tutar bulunamadı.");
    }
}
