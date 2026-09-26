using FisAsistan.Application.Receipts.Parsing;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

public class TurkishNumberNormalizerTests
{
    [Theory]
    [InlineData("62,50", 62.50)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("12.345,67", 12345.67)]
    [InlineData("1234.56", 1234.56)]     // US formatı da tolere edilir
    [InlineData("1,234.56", 1234.56)]
    [InlineData("100", 100)]
    [InlineData("0,50", 0.50)]
    public void TryParse_ValidFormats_ReturnsExpectedDecimal(string input, decimal expected)
    {
        var result = TurkishNumberNormalizer.TryParse(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("   ")]
    public void TryParse_InvalidInput_ReturnsNull(string? input)
    {
        var result = TurkishNumberNormalizer.TryParse(input);
        result.Should().BeNull();
    }
}
