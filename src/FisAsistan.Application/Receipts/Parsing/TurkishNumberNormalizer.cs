using System.Globalization;
using System.Text.RegularExpressions;

namespace FisAsistan.Application.Receipts.Parsing;

/// <summary>
/// Türk fişlerinde görülen sayı formatlarını ("1.234,56", "1234,56", "1234.56", "1234")
/// standart bir decimal değere normalize eder. OCR bazen ondalık ayıracını da
/// bozabildiği için (örn. "1234;56" veya "1234 56") birkaç tolere edilebilir varyant da dener.
/// </summary>
public static class TurkishNumberNormalizer
{
    private static readonly Regex NumberCharsOnly = new(@"[^0-9.,]", RegexOptions.Compiled);

    /// <summary>Bir metin parçasından decimal ayrıştırmayı dener. Başarısızsa null döner (uydurmaz).</summary>
    public static decimal? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = NumberCharsOnly.Replace(raw.Trim(), string.Empty);
        if (cleaned.Length == 0)
        {
            return null;
        }

        var hasDot = cleaned.Contains('.');
        var hasComma = cleaned.Contains(',');

        string normalized;
        if (hasDot && hasComma)
        {
            // Hangisi son geçiyorsa o ondalık ayıracıdır (1.234,56 -> ',' ondalık; 1,234.56 -> '.' ondalık)
            var lastDot = cleaned.LastIndexOf('.');
            var lastComma = cleaned.LastIndexOf(',');
            if (lastComma > lastDot)
            {
                normalized = cleaned.Replace(".", string.Empty).Replace(',', '.');
            }
            else
            {
                normalized = cleaned.Replace(",", string.Empty);
            }
        }
        else if (hasComma)
        {
            // Tek ',' varsa ondalık ayıracı kabul edilir (Türk formatı): 1234,56
            normalized = cleaned.Replace(".", string.Empty).Replace(',', '.');
        }
        else
        {
            // Sadece '.' veya hiçbiri yok — zaten geçerli invariant formatta olabilir.
            normalized = cleaned;
        }

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }
}
