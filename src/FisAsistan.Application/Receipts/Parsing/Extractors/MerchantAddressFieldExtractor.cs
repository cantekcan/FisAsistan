using System.Text.RegularExpressions;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// Adres satırını, tipik Türkçe adres bileşenlerini (MAH, CAD, SOK, NO, BLV, APT) içeren
/// satırları arayarak bulmaya çalışan bir heuristiktir.
/// </summary>
public class MerchantAddressFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.MerchantAddress;

    private static readonly Regex AddressHints = new(
        @"\b(MAH\.?|MAHALLESİ|CAD\.?|CADDESİ|SOK\.?|SOKAK|BLV\.?|BULVARI|NO\s*:?\s*\d|APT\.?|KAT\s*:?\s*\d)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        foreach (var line in context.Lines.Take(8))
        {
            if (AddressHints.IsMatch(line))
            {
                // Gerçek fiş fotoğraflarında satırın başında arka plan/kadraj gürültüsünden
                // kaynaklanan anlamsız semboller olabilir (ör. "_»'—.w HARMAN MH..."); asıl
                // adres metni bozulmadan önce bu gürültü temizlenir.
                var cleaned = OcrNoiseFilter.StripLeadingNoise(line);
                return ExtractionCandidate.Success(
                    cleaned,
                    $"'{line}' satırında tipik adres ifadeleri (Mah./Cad./Sok./No) tespit edildi.");
            }
        }

        return ExtractionCandidate.NotFound("Metinde adres bileşeni (Mah./Cad./Sok./No) içeren bir satır bulunamadı.");
    }
}
