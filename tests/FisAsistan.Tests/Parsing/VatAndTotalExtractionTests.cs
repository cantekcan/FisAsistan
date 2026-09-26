using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Application.Receipts.Parsing.Extractors;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

public class VatAndTotalExtractionTests
{
    [Fact]
    public void VatLineExtractor_MultipleRates_ExtractsAllLines()
    {
        var text = "ARA TOPLAM 57,50\nKDV %1 KDV 0,50\nKDV %10 KDV 4,50\nGENEL TOPLAM 62,50";
        var context = new ParsedOcrContext(text);
        var sut = new VatLineExtractor();

        var result = sut.Extract(context);

        result.Should().HaveCount(2);
        result.Should().ContainSingle(v => v.RatePercent == 1m && v.VatAmount == 0.50m);
        result.Should().ContainSingle(v => v.RatePercent == 10m && v.VatAmount == 4.50m);
    }

    [Fact]
    public void TotalAmountExtractor_PrefersGenelToplamOverAraToplam()
    {
        var text = "ARA TOPLAM 57,50\nGENEL TOPLAM 62,50";
        var context = new ParsedOcrContext(text);
        var sut = new TotalAmountFieldExtractor();

        var result = sut.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("62.50");
    }

    [Fact]
    public void TotalAmountExtractor_OnlyToplamLabel_FallsBackCorrectly()
    {
        var text = "URUN 1  10,00\nTOPLAM 10,00";
        var context = new ParsedOcrContext(text);
        var sut = new TotalAmountFieldExtractor();

        var result = sut.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("10.00");
    }

    [Fact]
    public void TotalAmountExtractor_NoLabel_ReturnsNotFound()
    {
        var context = new ParsedOcrContext("MIGROS TICARET A.S.\nEKMEK 12,50");
        var sut = new TotalAmountFieldExtractor();

        var result = sut.Extract(context);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public void SubTotalExtractor_FindsAraToplam()
    {
        var context = new ParsedOcrContext("ARA TOPLAM 100,00\nGENEL TOPLAM 120,00");
        var sut = new SubTotalFieldExtractor();

        var result = sut.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("100.00");
    }
}
