using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// Satıcı adını heuristik olarak bulur. İki aşamalı çalışır:
/// 1) Belgenin üst kısmında (ilk ~10 satır) şirket unvanı eki (A.Ş, LTD, TİC vb.) içeren
///    bir satır varsa, bu en güvenilir sinyaldir ve öncelikli olarak kullanılır — gerçek
///    fiş fotoğraflarında bu satırdan önce arka plan/kadraj gürültüsünden kaynaklanan
///    anlamsız OCR satırları çıkabildiği için yalnızca "ilk satır" varsayımı yeterli değildir.
/// 2) Bulunamazsa, ilk birkaç satır arasında etiket içermeyen, sayı ağırlıklı olmayan ve
///    OCR gürültüsü gibi görünmeyen ilk satır seçilir.
/// Bu kesin bir kural değil, açıkça bir heuristik olduğu Explanation alanında belirtilir;
/// kullanıcı mutlaka doğrulamalıdır.
/// </summary>
public class MerchantNameFieldExtractor : IFieldExtractor
{
    public ReceiptFieldName FieldName => ReceiptFieldName.MerchantName;

    private static readonly string[] StopWords =
    {
        "VKN", "VD", "TARİH", "SAAT", "FİŞ", "FIS", "ADRES", "TEL", "FATURA", "MERSİS"
    };

    private static readonly string[] CompanySuffixHints =
    {
        "A.Ş", "A.S", "ANONİM", "LTD", "LİMİTED", "TİC", "TIC", "ŞTİ", "STI",
        "MAĞAZA", "MAGAZA", "MARKET", "GIDA"
    };

    private const int TopSectionLineCount = 10;

    public ExtractionCandidate Extract(ParsedOcrContext context)
    {
        var topSection = context.Lines.Take(TopSectionLineCount).ToList();

        // 1. aşama: şirket unvanı eki içeren, gürültü olmayan bir satır ara (en güvenilir sinyal).
        foreach (var line in topSection)
        {
            if (OcrNoiseFilter.IsLikelyGarbage(line))
            {
                continue;
            }

            var upper = line.ToUpperInvariant();
            if (CompanySuffixHints.Any(hint => upper.Contains(hint)))
            {
                var cleaned = OcrNoiseFilter.StripLeadingNoise(line);
                return ExtractionCandidate.Success(
                    cleaned,
                    $"'{line}' satırında şirket unvanı eki (A.Ş/LTD/TİC vb.) tespit edildi — bu genelde " +
                    "en güvenilir satıcı adı sinyalidir (heuristik — mutlaka gözden geçirin).");
            }
        }

        // 2. aşama: eski davranış — etiket/gürültü içermeyen, sayı ağırlıklı olmayan ilk satır.
        foreach (var line in topSection)
        {
            if (line.Length < 3 || OcrNoiseFilter.IsLikelyGarbage(line))
            {
                continue;
            }

            var upper = line.ToUpperInvariant();
            if (StopWords.Any(sw => upper.Contains(sw)))
            {
                continue;
            }

            if (line.Any(char.IsDigit) && line.Count(char.IsDigit) > line.Length / 2)
            {
                // Sayı ağırlıklı satır (VKN, tarih vb.) muhtemelen işletme adı değildir.
                continue;
            }

            var cleaned = OcrNoiseFilter.StripLeadingNoise(line);
            return ExtractionCandidate.Success(
                cleaned,
                $"Belgenin üst kısmından, etiket/gürültü içermeyen ve sayı ağırlıklı olmayan satır " +
                $"(heuristik — mutlaka gözden geçirin): '{line}'.");
        }

        return ExtractionCandidate.NotFound("Belgenin üst kısmında güvenilir bir satıcı adı satırı tespit edilemedi.");
    }
}
