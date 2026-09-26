using System.Text.RegularExpressions;

namespace FisAsistan.Application.Receipts.Parsing.Extractors;

/// <summary>
/// Gerçek fiş fotoğraflarında (özellikle fişin dışındaki arka plan dokusu/nesneler de kadraja
/// girdiğinde) Tesseract bazen anlamsız sembol yığınları üretir (ör. "— ”/ K '/ YU MN").
/// Bu yardımcı sınıf, satıcı adı/adres gibi metinsel alanları ararken bu tür gürültülü
/// satırları elemeye ve satır başındaki gürültülü karakterleri temizlemeye yarar.
/// </summary>
internal static class OcrNoiseFilter
{
    // Türk fişlerinde satıcı adı/adres bilgisi neredeyse her zaman BÜYÜK HARFLERLE yazılır.
    // Bu yüzden satır başında büyük harf veya rakama ulaşana kadar her şeyi (semboller ve
    // OCR'ın yanlışlıkla ürettiği tekil küçük harfler dahil) gürültü sayıp temizliyoruz.
    private static readonly Regex LeadingNoiseRegex = new(
        @"^[^A-ZÇĞİÖŞÜ0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Bir satırın anlamlı metin mi yoksa OCR gürültüsü mü olduğunu kabaca tahmin eder:
    /// harf oranı çok düşükse (semboller/rastgele büyük harf parçaları ağırlıktaysa) gürültü sayılır.
    /// </summary>
    public static bool IsLikelyGarbage(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3)
        {
            return true;
        }

        var letters = trimmed.Count(char.IsLetter);
        var nonSpace = trimmed.Count(c => !char.IsWhiteSpace(c));
        if (nonSpace == 0)
        {
            return true;
        }

        var letterRatio = (double)letters / nonSpace;

        // Gerçek kelimeler genelde art arda birkaç harf içerir; "K '/ YU MN" gibi satırlarda
        // tek/iki harfli parçacıklar sembollerle ayrılmıştır — ortalama "kelime" uzunluğu düşüktür.
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var avgWordLength = words.Length > 0 ? words.Average(w => w.Length) : 0;

        return letterRatio < 0.55 || avgWordLength < 2.2;
    }

    /// <summary>Satır başındaki gürültülü sembolleri temizler (ör. "_»'—.w HARMAN..." -> "HARMAN...").</summary>
    public static string StripLeadingNoise(string line) => LeadingNoiseRegex.Replace(line, string.Empty).TrimStart();
}
