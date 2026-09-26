using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Application.Receipts.Parsing.Extractors;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

public class DateFieldExtractorTests
{
    private readonly DateFieldExtractor _sut = new();

    [Theory]
    [InlineData("TARIH: 15.03.2026", "2026-03-15")]
    [InlineData("15/03/2026 14:32", "2026-03-15")]
    [InlineData("15-03-2026", "2026-03-15")]
    [InlineData("2026-03-15", "2026-03-15")]
    public void Extract_ValidDateFormats_ReturnsIsoDate(string line, string expectedIso)
    {
        var context = new ParsedOcrContext(line);
        var result = _sut.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be(expectedIso);
    }

    [Fact]
    public void Extract_NoDateInText_ReturnsNotFound()
    {
        var context = new ParsedOcrContext("MIGROS TICARET A.S.\nGENEL TOPLAM 62,50");
        var result = _sut.Extract(context);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public void Extract_ImplausibleYear_IsIgnored()
    {
        // Belge numarası gibi tarih formatına uyan ama makul olmayan bir sayı dizisi
        var context = new ParsedOcrContext("FIS NO: 01.01.1850");
        var result = _sut.Extract(context);

        result.Found.Should().BeFalse();
    }
}
