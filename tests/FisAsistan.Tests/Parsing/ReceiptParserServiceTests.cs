using System.Linq;
using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

public class ReceiptParserServiceTests
{
    private readonly ReceiptParserService _sut = new();

    [Fact]
    public void Parse_WellFormedReceipt_ExtractsAllFieldsAndNoIssues()
    {
        var text = """
            MIGROS TICARET A.S.
            BARBAROS MAH. AHI EVRAN CAD. NO:1
            VKN: 1234567890
            TARIH: 15.03.2026 SAAT: 14:32
            FIS NO: 000123
            ARA TOPLAM 57,50
            KDV %1 KDV 0,50
            KDV %10 KDV 4,50
            GENEL TOPLAM 62,50
            NAKIT
            """;

        var result = _sut.Parse(text);

        result.Fields[ReceiptFieldName.MerchantName].Found.Should().BeTrue();
        result.Fields[ReceiptFieldName.TaxId].Value.Should().Be("1234567890");
        result.Fields[ReceiptFieldName.ReceiptDate].Value.Should().Be("2026-03-15");
        result.Fields[ReceiptFieldName.TotalAmount].Value.Should().Be("62.50");
        result.VatLines.Should().HaveCount(2);
        result.Issues.Should().BeEmpty("ara toplam + KDV = genel toplam ve tüm zorunlu alanlar bulundu");
    }

    [Fact]
    public void Parse_MismatchedTotals_ProducesWarningIssue()
    {
        var text = "ARA TOPLAM 50,00\nKDV %10 5,00\nGENEL TOPLAM 100,00";

        var result = _sut.Parse(text);

        result.Issues.Should().Contain(i => i.RuleCode == "TOTAL_MISMATCH");
    }

    [Fact]
    public void Parse_MissingTotal_ProducesErrorIssue()
    {
        var text = "MIGROS TICARET A.S.\nTARIH: 15.03.2026";

        var result = _sut.Parse(text);

        result.Issues.Should().Contain(i => i.RuleCode == "TOTAL_NOT_FOUND" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Parse_EmptyOcrText_DoesNotThrow_AndFlagsMissingFields()
    {
        var result = _sut.Parse(string.Empty);

        // Currency alanı hariç: hiçbir sembol bulunamazsa bilinçli olarak varsayılan TRY atanır.
        result.Fields.Where(f => f.Key != ReceiptFieldName.Currency).Should()
            .OnlyContain(f => !f.Value.Found);
        result.Fields[ReceiptFieldName.Currency].Value.Should().Be("TRY");
        result.Issues.Should().Contain(i => i.RuleCode == "TOTAL_NOT_FOUND");
        result.Issues.Should().Contain(i => i.RuleCode == "DATE_NOT_FOUND");
    }

    [Fact]
    public void Parse_GarbledOcrText_DoesNotThrow()
    {
        // Bozuk/okunamayan OCR çıktısını simüle eder — parser hiçbir alanı bulamamalı ama patlamamalı.
        var text = "###@@@ !!! ???///\n%%%%% &&&&&";

        var act = () => _sut.Parse(text);

        act.Should().NotThrow();
    }

    [Fact]
    public void Parse_MissingTaxId_ProducesWarning()
    {
        var text = "MIGROS TICARET A.S.\nTARIH: 15.03.2026\nGENEL TOPLAM 10,00";

        var result = _sut.Parse(text);

        result.Issues.Should().Contain(i => i.RuleCode == "TAXID_NOT_FOUND");
    }
}
