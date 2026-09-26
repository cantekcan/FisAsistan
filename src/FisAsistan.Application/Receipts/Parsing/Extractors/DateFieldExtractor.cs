using System.Globalization;
using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Fiş tarihini bulur: dd.mm.yyyy, dd/mm/yyyy, dd-mm-yyyy ve yyyy-mm-dd formatlarını dener.</summary>
public class DateFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.ReceiptDate;

    private static readonly Regex DmyRegex = new(
        @"\b(?<d>[0-3]?\d)[.\-/](?<m>[01]?\d)[.\-/](?<y>\d{2,4})\b", RegexOptions.Compiled);

    private static readonly Regex YmdRegex = new(
        @"\b(?<y>\d{4})[.\-/](?<m>[01]?\d)[.\-/](?<d>[0-3]?\d)\b", RegexOptions.Compiled);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var line in context.Lines)
        {
            var match = DmyRegex.Match(line);
            var isYmd = false;
            if (!match.Success)
            {
                match = YmdRegex.Match(line);
                isYmd = true;
            }

            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups["d"].Value, out var day) ||
                !int.TryParse(match.Groups["m"].Value, out var month) ||
                !int.TryParse(match.Groups["y"].Value, out var year))
            {
                continue;
            }

            if (year < 100)
            {
                year += 2000;
            }

            if (month < 1 || month > 12 || day < 1 || day > 31)
            {
                continue;
            }

            if (year < 2000 || year > DateTime.UtcNow.Year + 1)
            {
                // Fiş tarihleri için makul olmayan bir yıl — muhtemelen yanlış eşleşme (belge no vb.)
                continue;
            }

            try
            {
                var date = new DateTime(year, month, day);
                var formatFound = isYmd ? "yyyy-aa-gg" : "gg.aa.yyyy";
                return ExtractionCandidate.Success(
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    $"'{line}' satırında {formatFound} formatına uyan tarih bulundu: '{match.Value}'.");
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }
        }

        return ExtractionCandidate.NotFound("Metinde geçerli bir tarih deseni (gg.aa.yyyy / yyyy-aa-gg) bulunamadı.");
    }
}
