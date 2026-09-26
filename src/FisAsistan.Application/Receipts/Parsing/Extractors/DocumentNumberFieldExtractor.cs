using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Fiş/belge numarasını "FİŞ NO", "BELGE NO", "EKÜ NO", "FN" gibi etiketlerin yanından arar.</summary>
public class DocumentNumberFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.DocumentNumber;

    private static readonly Regex Regex = new(
        @"(FİŞ\s*NO|FIS\s*NO|BELGE\s*NO|EKÜ\s*NO|EKU\s*NO|\bFN\b|FATURA\s*NO|MAKBUZ\s*NO)\D{0,10}(?<num>[A-Z0-9\-/]{3,20})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var line in context.Lines)
        {
            var match = Regex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var value = match.Groups["num"].Value.Trim();
            return ExtractionCandidate.Success(
                value,
                $"'{line}' satırında belge numarası etiketi bulundu: '{value}'.");
        }

        return ExtractionCandidate.NotFound("Metinde 'FİŞ NO', 'BELGE NO' gibi bir etiketin yanında belge numarası bulunamadı.");
    }
}
