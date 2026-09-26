using System.Text;
using FisAsistan.Application.Receipts.Dtos;
using FisAsistan.Infrastructure.Export;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Export;

public class ReceiptExportServiceTests
{
    private readonly ReceiptExportService _sut = new();

    private static List<ReceiptExportRowDto> SampleRows() => new()
    {
        new ReceiptExportRowDto
        {
            ReceiptId = "abc-123",
            MerchantName = "MIGROS",
            TaxId = "1234567890",
            ReceiptDate = "2026-03-15",
            TotalAmount = "62.50",
            Currency = "TRY",
            Status = "Approved"
        }
    };

    [Fact]
    public void ExportToCsv_ProducesNonEmptyCsvWithHeaderAndRow()
    {
        var bytes = _sut.ExportToCsv(SampleRows());
        var text = Encoding.UTF8.GetString(bytes);

        bytes.Should().NotBeEmpty();
        text.Should().Contain("ReceiptId");
        text.Should().Contain("MIGROS");
        text.Should().Contain("1234567890");
    }

    [Fact]
    public void ExportToXlsx_ProducesValidXlsxWorkbook()
    {
        var bytes = _sut.ExportToXlsx(SampleRows());

        bytes.Should().NotBeEmpty();
        // xlsx dosyaları ZIP formatındadır; "PK" imzası ile başlar.
        bytes[0].Should().Be((byte)'P');
        bytes[1].Should().Be((byte)'K');
    }

    [Fact]
    public void ExportToCsv_EmptyList_ProducesHeaderOnly()
    {
        var bytes = _sut.ExportToCsv(new List<ReceiptExportRowDto>());
        var text = Encoding.UTF8.GetString(bytes);

        text.Should().Contain("ReceiptId");
    }
}
