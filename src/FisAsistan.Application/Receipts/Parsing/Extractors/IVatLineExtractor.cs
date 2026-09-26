namespace FisAsistan.Application.Receipts.Parsing.Extractors;

public interface IVatLineExtractor
{
    List<VatLineCandidate> Extract(ParsedOcrContext context);
}
