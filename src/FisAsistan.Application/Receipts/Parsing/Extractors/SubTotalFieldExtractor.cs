using System.Globalization;
using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>Ara toplamı ("ARA TOPLAM") bulur.</summary>
public class SubTotalFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.SubTotal;

    private static readonly Regex Label = new(
        @"(ARA\s*TOPLAM|ARATOPLAM)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var line in context.Lines)
        {
            var (found, raw, amount) = AmountLineHelper.TryFindAmountAfterLabel(line, Label);
            if (found && amount.HasValue)
            {
                return ExtractionCandidate.Success(
                    amount.Value.ToString(CultureInfo.InvariantCulture),
                    $"'{line}' satırında 'ARA TOPLAM' etiketiyle bulundu: '{raw}'.");
            }
        }

        return ExtractionCandidate.NotFound("Metinde 'ARA TOPLAM' etiketiyle bir tutar bulunamadı.");
    }
}
