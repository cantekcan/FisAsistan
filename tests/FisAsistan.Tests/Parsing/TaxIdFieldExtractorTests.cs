using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Application.Receipts.Parsing.Extractors;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

public class TaxIdFieldExtractorTests
{
    private readonly TaxIdFieldExtractor _sut = new();

    [Fact]
    public void Extract_LabeledVkn_ReturnsValue()
    {
        var context = new ParsedOcrContext("VKN: 1234567890");
        var result = _sut.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("1234567890");
    }

    [Fact]
    public void Extract_ValidTckn_PassesChecksum()
    {
        // 10000000146 gerçek bir TCKN algoritma-geçerli test numarasıdır (kamuya açık örnek).
        var context = new ParsedOcrContext("TCKN: 10000000146");
        var result = _sut.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("10000000146");
    }

    [Fact]
    public void Extract_InvalidTcknChecksum_IsNotAcceptedAsTckn()
    {
        // 11 haneli ama checksum'ı geçersiz olan ve VKN olamayacak (11 hane) bir sayı bulunamamalı.
        var context = new ParsedOcrContext("NO: 12345678901");
        var result = _sut.Extract(context);

        result.Found.Should().BeFalse();
    }

    [Fact]
    public void IsValidTckn_KnownInvalidNumber_ReturnsFalse()
    {
        TaxIdFieldExtractor.IsValidTckn("11111111111").Should().BeFalse();
    }

    [Fact]
    public void Extract_NoTaxId_ReturnsNotFound()
    {
        var context = new ParsedOcrContext("MIGROS TICARET A.S.\nGENEL TOPLAM 62,50");
        var result = _sut.Extract(context);

        result.Found.Should().BeFalse();
    }
}
