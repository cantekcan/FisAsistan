using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Fiş saatini bulur: hh:mm veya hh:mm:ss.</summary>
public class TimeFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.ReceiptTime;

    private static readonly Regex TimeRegex = new(
        @"\b(?<h>[0-2]?\d):(?<m>[0-5]\d)(:(?<s>[0-5]\d))?\b", RegexOptions.Compiled);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var line in context.Lines)
        {
            var match = TimeRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups["h"].Value, out var hour) || hour > 23)
            {
                continue;
            }

            var minute = match.Groups["m"].Value;
            var second = match.Groups["s"].Success ? match.Groups["s"].Value : "00";
            var value = $"{hour:D2}:{minute}:{second}";

            return ExtractionCandidate.Success(
                value,
                $"'{line}' satırında saat deseni bulundu: '{match.Value}'.");
        }

        return ExtractionCandidate.NotFound("Metinde saat deseni (ss:dd) bulunamadı.");
    }
}
