namespace FisAsistan.Infrastructure.Ocr;

public class TesseractOptions
{
    public const string SectionName = "Tesseract";

    /// <summary>*.traineddata dosyalarının bulunduğu klasör.</summary>
    public string TessDataPath { get; set; } = "tessdata";

    /// <summary>Tesseract dil kodu, ör. "tur+eng" (Türkçe fişlerde bazen Latin/İngilizce marka adları da geçer).</summary>
    public string Languages { get; set; } = "tur+eng";
}
