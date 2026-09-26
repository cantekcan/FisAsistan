using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// VKN (10 haneli) veya TCKN (11 haneli, algoritma ile doğrulanabilir) arar.
/// Önce "VKN/VD/Vergi No/TCKN" gibi etiketlere yakın sayıları, bulunamazsa
/// bağımsız 10-11 haneli sayı dizilerini dener.
/// </summary>
public class TaxIdFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.TaxId;

    private static readonly Regex LabeledRegex = new(
        @"(VKN|V\.K\.N|VD|VERGİ\s*NO|VERGİ\s*DAİRESİ|TCKN|T\.C\.?\s*KİMLİK)\D{0,15}(?<num>\d{9,11})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BareNumberRegex = new(@"\b\d{10,11}\b", RegexOptions.Compiled);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var line in context.Lines)
        {
            var match = LabeledRegex.Match(line);
            if (match.Success)
            {
                var num = match.Groups["num"].Value;
                var kind = ClassifyAndValidate(num);
                if (kind != null)
                {
                    return ExtractionCandidate.Success(
                        num,
                        $"'{line}' satırında etiketli {kind} bulundu: '{num}'.");
                }
            }
        }

        foreach (var line in context.Lines)
        {
            foreach (Match m in BareNumberRegex.Matches(line))
            {
                var num = m.Value;
                var kind = ClassifyAndValidate(num);
                if (kind != null)
                {
                    return ExtractionCandidate.Success(
                        num,
                        $"'{line}' satırında etiketsiz ama formatı geçerli {kind} bulundu: '{num}'.");
                }
            }
        }

        return ExtractionCandidate.NotFound("Geçerli formatta VKN (10 hane) veya TCKN (11 hane, doğrulama algoritması) bulunamadı.");
    }

    /// <summary>10 haneliyse VKN (sadece format kontrolü), 11 haneliyse TCKN (checksum algoritması) olarak doğrular.</summary>
    private static string? ClassifyAndValidate(string num)
    {
        if (num.Length == 10 && num.All(char.IsDigit) && num[0] != '0')
        {
            return "VKN";
        }

        if (num.Length == 11 && IsValidTckn(num))
        {
            return "TCKN";
        }

        return null;
    }

    public static bool IsValidTckn(string tckn)
    {
        if (tckn.Length != 11 || !tckn.All(char.IsDigit) || tckn[0] == '0')
        {
            return false;
        }

        var digits = tckn.Select(c => c - '0').ToArray();

        var oddSum = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var evenSum = digits[1] + digits[3] + digits[5] + digits[7];

        var d10 = ((oddSum * 7) - evenSum) % 10;
        if (d10 < 0)
        {
            d10 += 10;
        }

        if (d10 != digits[9])
        {
            return false;
        }

        var totalSum = digits.Take(10).Sum();
        var d11 = totalSum % 10;

        return d11 == digits[10];
    }
}
