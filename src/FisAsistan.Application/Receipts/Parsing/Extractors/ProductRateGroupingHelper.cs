using System.Text.RegularExpressions;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// Birçok Türk yazar kasa fişinde (ör. BİM, A101, Migros marketleri) KDV, bir toplam satırında
/// değil, HER ÜRÜN satırının yanında bir oran kodu ("%08", "%18" gibi) olarak gösterilir ve
/// dip toplamda tek bir "TOPKDV" rakamı bulunur — hangi oranın ne kadar tuttuğu açıkça yazmaz.
///
/// Mali müşavirin talep ettiği yöntem: ürünleri tek tek değil, KDV oranına göre GRUPLAYIP
/// TOPLAYARAK muhasebeleştirmek (ör. tüm %8'lik ürünlerin toplamı bir satır, %18'lik ürünlerin
/// toplamı başka bir satır). Bu sınıf tam olarak bunu yapar: ürün satırlarını tek tek saklamaz,
/// yalnızca oranlarına göre grup toplamlarını üretir.
///
/// Ürün fiyatları KDV dahil yazıldığından (Türkiye'de yaygın uygulama), toplamdan geriye doğru
/// matrah ve KDV tutarı hesaplanır: KDV = ToplamTutar × oran / (100 + oran).
/// </summary>
internal static class ProductRateGroupingHelper
{
    private static readonly Regex RateCodeRegex = new(@"%\s*(?<rate>\d{1,2})\b", RegexOptions.Compiled);

    private static readonly Regex PriceTokenRegex = new(
        @"\d{1,3}(?:[.,]\d{3})*[.,]\d{2}|\d+[.,]\d{2}", RegexOptions.Compiled);

    // Türkiye'de fişlerde görülen bilinen KDV oranları — rastgele bir yüzdenin (ör. indirim
    // yüzdesi) yanlışlıkla KDV oranı sanılmasını önlemek için bir güvenlik sınırı.
    private static readonly HashSet<decimal> KnownVatRates = new() { 1m, 8m, 10m, 18m, 20m };

    public static List<VatLineCandidate> ExtractByGroupingProductLines(ParsedOcrContext context)
    {
        var sumsByRate = new Dictionary<decimal, decimal>();
        var lineCountByRate = new Dictionary<decimal, int>();

        foreach (var line in context.Lines)
        {
            if (line.Contains("İNDİRİM", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("INDIRIM", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("KDV", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("TOPLAM", StringComparison.OrdinalIgnoreCase))
            {
                // Toplam/indirim/KDV özet satırları ürün satırı değildir, gruplamaya dahil edilmez.
                continue;
            }

            var rateMatch = RateCodeRegex.Match(line);
            if (!rateMatch.Success)
            {
                continue;
            }

            var rate = TurkishNumberNormalizer.TryParse(rateMatch.Groups["rate"].Value);
            if (rate is null || !KnownVatRates.Contains(rate.Value))
            {
                continue;
            }

            // Fiyat genelde satırın sonunda, oran koduyla aynı satırda yer alır.
            var priceMatch = PriceTokenRegex.Match(line);
            if (!priceMatch.Success)
            {
                continue;
            }

            var price = TurkishNumberNormalizer.TryParse(priceMatch.Value);
            if (price is null || price <= 0)
            {
                continue;
            }

            sumsByRate[rate.Value] = sumsByRate.GetValueOrDefault(rate.Value) + price.Value;
            lineCountByRate[rate.Value] = lineCountByRate.GetValueOrDefault(rate.Value) + 1;
        }

        var results = new List<VatLineCandidate>();
        foreach (var (rate, grossTotal) in sumsByRate.OrderBy(kv => kv.Key))
        {
            // Fiyatlar KDV dahil olduğundan, toplamdan geri hesaplama yapılır.
            var vatAmount = Math.Round(grossTotal * rate / (100 + rate), 2);
            var baseAmount = grossTotal - vatAmount;

            results.Add(new VatLineCandidate
            {
                RatePercent = rate,
                BaseAmount = Math.Round(baseAmount, 2),
                VatAmount = vatAmount,
                Explanation = $"Fişte %{rate} KDV kodlu {lineCountByRate[rate]} ürün satırı bulundu, " +
                              $"KDV dahil toplamları {grossTotal:0.00} olarak toplandı. Matrah ve KDV tutarı " +
                              $"bu toplamdan geriye doğru hesaplandı (ürünler tek tek değil, oran bazında " +
                              $"gruplanmıştır) — lütfen doğrulayın."
            });
        }

        return results;
    }
}
