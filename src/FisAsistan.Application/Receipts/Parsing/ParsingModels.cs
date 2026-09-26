using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing;

/// <summary>OCR'dan gelen ham metnin, satırlara bölünmüş ve parser'ların paylaştığı hali.</summary>
public class ParsedOcrContext
{
    public string RawText { get; }
    public IReadOnlyList<string> Lines { get; }

    public ParsedOcrContext(string rawText)
    {
        RawText = rawText ?? string.Empty;
        Lines = RawText
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }
}

/// <summary>
/// Bir alan çıkarıcının ürettiği sonuç. Güvenilirlik puanı yerine, sonucun
/// hangi kurala/metne dayandığını açıklayan bir metin taşır (açıklanabilirlik).
/// </summary>
public class ExtractionCandidate
{
    public bool Found { get; init; }
    public string? Value { get; init; }
    public string? Explanation { get; init; }

    public static ExtractionCandidate NotFound(string reason) =>
        new() { Found = false, Value = null, Explanation = reason };

    public static ExtractionCandidate Success(string value, string explanation) =>
        new() { Found = true, Value = value, Explanation = explanation };
}

/// <summary>Tek bir alanı ham OCR bağlamından çıkarmaya çalışan kural sınıfı. Her alan ayrı bir sınıftır.</summary>
public interface IFieldExtractor
{
    ReceiptFieldName FieldName { get; }
    ExtractionCandidate Extract(ParsedOcrContext context);
}

public class VatLineCandidate
{
    public decimal RatePercent { get; init; }
    public decimal? BaseAmount { get; init; }
    public decimal VatAmount { get; init; }
    public string Explanation { get; init; } = string.Empty;
}

public class ValidationIssueCandidate
{
    public ReceiptFieldName? FieldName { get; init; }
    public IssueSeverity Severity { get; init; }
    public string RuleCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public class ReceiptParseResult
{
    public Dictionary<ReceiptFieldName, ExtractionCandidate> Fields { get; } = new();
    public List<VatLineCandidate> VatLines { get; } = new();
    public List<ValidationIssueCandidate> Issues { get; } = new();
}
