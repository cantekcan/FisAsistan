using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

/// <summary>Farklı market/işletme formatlarını simüle eden testler.</summary>
public class AlternativeReceiptFormatsTests
{
    private readonly ReceiptParserService _sut = new();

    [Fact]
    public void Parse_OdenecekTutarVariant_IsRecognizedAsTotal()
    {
        var text = "A101 MARKET\nTARİH: 01.01.2026\nÖDENECEK TUTAR: 25,00";

        var result = _sut.Parse(text);

        result.Fields[ReceiptFieldName.TotalAmount].Found.Should().BeTrue();
        result.Fields[ReceiptFieldName.TotalAmount].Value.Should().Be("25.00");
    }

    [Fact]
    public void Parse_ReversedVatOrder_PercentBeforeKdv_IsRecognized()
    {
        var text = "TOPLAM 12,00\n%20 KDV 2,00";

        var result = _sut.Parse(text);

        result.VatLines.Should().ContainSingle(v => v.RatePercent == 20m && v.VatAmount == 2.00m);
    }

    [Fact]
    public void Parse_CreditCardPayment_IsRecognized()
    {
        var text = "TOPLAM 100,00\nKREDI KARTI";

        var result = _sut.Parse(text);

        result.Fields[ReceiptFieldName.PaymentMethod].Value.Should().Be("Kredi Kartı");
    }

    [Fact]
    public void Parse_YmdDateFormat_IsRecognized()
    {
        var text = "TARIH: 2026-01-05\nTOPLAM 10,00";

        var result = _sut.Parse(text);

        result.Fields[ReceiptFieldName.ReceiptDate].Value.Should().Be("2026-01-05");
    }

    [Fact]
    public void Parse_SirketUnvanindakiSMisreadAsDollar_DoesNotTriggerUsd()
    {
        // Gerçek bir OCR hatası: "BİM BİRLEŞİK MAĞAZALAR A.Ş." unvanındaki "Ş" harfi
        // Tesseract tarafından "$" olarak okunmuştu ve yanlışlıkla USD tespit ediliyordu.
        var text = "BiM BiRLESiK MAGAZALAR A.$\nTOPLAM 26,15";

        var result = _sut.Parse(text);

        // USD tetiklenmemeli; sembol bulunamadığından varsayılan TRY atanmalı.
        result.Fields[ReceiptFieldName.Currency].Value.Should().Be("TRY");
    }

    [Fact]
    public void Parse_DollarSignNextToAmount_IsRecognizedAsUsd()
    {
        var text = "TOPLAM $10,00";

        var result = _sut.Parse(text);

        result.Fields[ReceiptFieldName.Currency].Value.Should().Be("USD");
    }

    [Fact]
    public void Parse_CurrencySymbolTl_IsRecognized()
    {
        var text = "TOPLAM 10,00 TL";

        var result = _sut.Parse(text);

        result.Fields[ReceiptFieldName.Currency].Value.Should().Be("TRY");
    }
}
