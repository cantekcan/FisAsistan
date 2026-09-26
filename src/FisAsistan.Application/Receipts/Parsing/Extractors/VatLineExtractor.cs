using System.Text.RegularExpressions;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// Türkiye'de fişlerde birden fazla KDV oranı (%1, %10, %20 vb.) görülebilir.
/// Öncelik: (1) "KDV %10 12,34" gibi dip toplamda AÇIKÇA etiketlenmiş oran+tutar satırları;
/// (2) bulunamazsa, mali müşavirin talep ettiği yönteme göre — ürünlerin her birinin yanındaki
/// oran kodundan ("%08" gibi) hareketle ürünleri KDV oranına göre GRUPLAYIP TOPLAYARAK hesaplama
/// (bkz. <see cref="ProductRateGroupingHelper"/>). Hiçbir durumda ürünler tek tek saklanmaz,
/// yalnızca oran bazında toplu satırlar üretilir.
/// </summary>
public class VatLineExtractor : IVatLineExtractor
{
    private static readonly Regex VatWithRateRegex = new(
        @"(KDV|VAT)\D{0,6}%\s*(?<rate>\d{1,2}(?:[.,]\d)?)|%\s*(?<rate2>\d{1,2}(?:[.,]\d)?)\D{0,6}(KDV|VAT)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public List<VatLineCandidate> Extract(ParsedOcrContext context)
    {
        var labeledResults = ExtractLabeledTotals(context);
        if (labeledResults.Count > 0)
        {
            return labeledResults;
        }

        // Fişte "KDV %X: tutar" şeklinde açık bir dip toplam yoksa (çoğu market fişinde durum budur),
        // ürünleri oran koduna göre gruplayıp toplayan yönteme geç.
        return ProductRateGroupingHelper.ExtractByGroupingProductLines(context);
    }

    private List<VatLineCandidate> ExtractLabeledTotals(ParsedOcrContext context)
    {
        var results = new List<VatLineCandidate>();
        var seenRates = new HashSet<decimal>();

        foreach (var line in context.Lines)
        {
            var match = VatWithRateRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var rateRaw = match.Groups["rate"].Success ? match.Groups["rate"].Value : match.Groups["rate2"].Value;
            var rate = TurkishNumberNormalizer.TryParse(rateRaw);
            if (rate is null || rate <= 0 || rate > 100)
            {
                continue;
            }

            if (!seenRates.Add(rate.Value))
            {
                continue;
            }

            var remainder = line[(match.Index + match.Length)..];
            var amount = TurkishNumberNormalizer.TryParse(ExtractFirstNumberToken(remainder));

            if (amount is null)
            {
                // Aynı satırda tutar yoksa satırın tamamında ara — bazı fişlerde tutar etiketten önce gelir.
                amount = TurkishNumberNormalizer.TryParse(ExtractFirstNumberToken(line));
            }

            if (amount is null)
            {
                continue;
            }

            results.Add(new VatLineCandidate
            {
                RatePercent = rate.Value,
                VatAmount = amount.Value,
                Explanation = $"'{line}' satırında %{rate.Value} KDV oranı için tutar bulundu: bulunan sayı bu satırdaki eşleşme.",
            });
        }

        return results;
    }

    private static readonly Regex NumberTokenRegex = new(
        @"\d{1,3}(?:[.,]\d{3})*[.,]\d{2}|\d+[.,]\d{2}", RegexOptions.Compiled);

    private static string ExtractFirstNumberToken(string text)
    {
        var m = NumberTokenRegex.Match(text);
        return m.Success ? m.Value : string.Empty;
    }
}
