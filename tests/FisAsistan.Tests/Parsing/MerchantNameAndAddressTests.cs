using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Application.Receipts.Parsing.Extractors;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

/// <summary>
/// Gerçek bir fiş fotoğrafında (fişin dışındaki arka plan dokusu kadraja girdiğinde)
/// karşılaşılan bir OCR sorununu simüle eder: gerçek metinden ÖNCE anlamsız gürültü
/// satırları çıkar ve/veya gerçek satırın başına gürültülü semboller karışır.
/// </summary>
public class MerchantNameAndAddressTests
{
    private readonly MerchantNameFieldExtractor _nameExtractor = new();
    private readonly MerchantAddressFieldExtractor _addressExtractor = new();

    private const string RealWorldNoisyReceipt = """
        — ”/ K '/ YU MN
        RN B B
        ; I U N Z
        H A AL
        BiM BiRLESiK MAGAZALAR A.$
        _»'—.w HARMAN MH.MEZARLIK SK. NO:4
        TALAS / KAYSERİ
        BUYUK MUKELLEFLER V.D 1750051846
        """;

    [Fact]
    public void MerchantName_SkipsBackgroundNoiseLines_FindsCompanyNameByLegalSuffix()
    {
        var context = new ParsedOcrContext(RealWorldNoisyReceipt);

        var result = _nameExtractor.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("BiM BiRLESiK MAGAZALAR A.$");
    }

    [Fact]
    public void MerchantAddress_StripsLeadingNoiseSymbols_FromMatchedLine()
    {
        var context = new ParsedOcrContext(RealWorldNoisyReceipt);

        var result = _addressExtractor.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("HARMAN MH.MEZARLIK SK. NO:4");
        result.Value.Should().NotContain("_»");
    }

    [Fact]
    public void MerchantName_CleanReceipt_StillWorksAsBeforeWithoutSuffixKeyword()
    {
        // Şirket unvanı eki (A.Ş vb.) olmayan temiz bir fişte eski davranış (ilk uygun satır) korunmalı.
        var context = new ParsedOcrContext("MIGROS TICARET\nTARIH: 15.03.2026");

        var result = _nameExtractor.Extract(context);

        result.Found.Should().BeTrue();
        result.Value.Should().Be("MIGROS TICARET");
    }

    [Theory]
    [InlineData("— ”/ K '/ YU MN")]
    [InlineData("RN B B")]
    [InlineData(";")]
    public void OcrNoiseFilter_GarbageLine_IsDetected(string line)
    {
        OcrNoiseFilter.IsLikelyGarbage(line).Should().BeTrue();
    }

    [Theory]
    [InlineData("BiM BiRLESiK MAGAZALAR A.$")]
    [InlineData("HARMAN MH.MEZARLIK SK. NO:4")]
    [InlineData("MIGROS TICARET A.S.")]
    public void OcrNoiseFilter_RealText_IsNotDetectedAsGarbage(string line)
    {
        OcrNoiseFilter.IsLikelyGarbage(line).Should().BeFalse();
    }
}
