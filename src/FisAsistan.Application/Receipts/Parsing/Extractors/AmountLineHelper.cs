using System.Text.RegularExpressions;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Bir satırda, verilen etiketten sonra gelen ilk parasal görünümlü sayıyı bulan ortak yardımcı.</summary>
internal static class AmountLineHelper
{
    private static readonly Regex AmountRegex = new(
        @"(?<amt>\d{1,3}(?:[.,]\d{3})*[.,]\d{2}|\d+[.,]\d{2}|\d+)", RegexOptions.Compiled);

    /// <summary>Satırda bir etiket eşleşiyorsa, o eşleşmeden sonraki ilk tutarı döndürür.</summary>
    public static (bool Found, string RawAmount, decimal? Amount) TryFindAmountAfterLabel(string line, Regex labelRegex)
    {
        var labelMatch = labelRegex.Match(line);
        if (!labelMatch.Success)
        {
            return (false, string.Empty, null);
        }

        var remainder = line[(labelMatch.Index + labelMatch.Length)..];
        var amountMatch = AmountRegex.Match(remainder);
        if (!amountMatch.Success)
        {
            // Aynı satırda yoksa etiketin kendi içindeki (örn. "%10" barındırmayan) tüm satırı dener
            amountMatch = AmountRegex.Match(line);
            if (!amountMatch.Success)
            {
                return (false, string.Empty, null);
            }
        }

        var raw = amountMatch.Groups["amt"].Value;
        var value = TurkishNumberNormalizer.TryParse(raw);
        return (value.HasValue, raw, value);
    }
}
