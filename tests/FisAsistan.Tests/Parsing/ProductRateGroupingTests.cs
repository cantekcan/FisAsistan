using FisAsistan.Application.Receipts.Parsing;
using FisAsistan.Application.Receipts.Parsing.Extractors;
using FisAsistan.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FisAsistan.Tests.Parsing;

/// <summary>
/// Mali müşavirin talep ettiği yöntem: ürünler tek tek değil, KDV oranına göre
/// gruplanıp toplanarak muhasebeleştirilir (ör. tüm %8'lik ürünlerin toplamı tek satır).
/// </summary>
public class ProductRateGroupingTests
{
    private readonly VatLineExtractor _sut = new();

    [Fact]
    public void Extract_ProductsWithPerLineRateCodes_GroupsAndSumsByRate_InsteadOfPerProduct()
    {
        // Gerçek bir BİM fişindeki formatı simüle eder: her ürünün yanında kendi KDV kodu var,
        // dip toplamda "TOPKDV" var ama hangi oranın ne kadar olduğu açık değil.
        var text = """
            PAT.CIPSI PATITO %08 *3,25
            PAT.CIPSI PATITO %08 *3,25
            SUT AROMALI %08 *1,00
            ALISVERIS POSETI %18 *0,25
            TOPKDV *1,96
            TOPLAM *26,15
            """;

        var context = new ParsedOcrContext(text);
        var result = _sut.Extract(context);

        // Tek tek 4 ürün satırı değil, yalnızca 2 grup (oran) satırı bekleniyor.
        result.Should().HaveCount(2);

        var rate8 = result.Should().ContainSingle(v => v.RatePercent == 8m).Subject;
        // 3,25 + 3,25 + 1,00 = 7,50 (KDV dahil toplam) -> geriye doğru KDV hesaplanır
        rate8.BaseAmount.Should().BeApproximately(6.94m, 0.01m);
        rate8.VatAmount.Should().BeApproximately(0.56m, 0.01m);

        var rate18 = result.Should().ContainSingle(v => v.RatePercent == 18m).Subject;
        rate18.VatAmount.Should().BeApproximately(0.04m, 0.01m);
    }

    [Fact]
    public void Extract_ExplicitLabeledVatTotals_TakesPriorityOverGrouping()
    {
        // Fişte zaten "KDV %10: 4,50" gibi açık bir dip toplam varsa, ürün gruplama
        // devreye girmemeli — açık değer her zaman önceliklidir.
        var text = "ARA TOPLAM 57,50\nKDV %10 KDV 4,50\nGENEL TOPLAM 62,50";

        var context = new ParsedOcrContext(text);
        var result = _sut.Extract(context);

        result.Should().ContainSingle();
        result[0].VatAmount.Should().Be(4.50m);
    }

    [Fact]
    public void Parse_TotalsUnreadable_ButVatGroupsKnown_DerivesSubtotalAndTotal()
    {
        // Gerçek dünya senaryosu: "TOPLAM" satırı OCR tarafından okunamadı (kırışık fotoğraf),
        // ama ürün satırları KDV oranına göre gruplanabildiğinden ara toplam ve genel toplam
        // bu matrah/KDV bilgisinden hesaplanarak kullanıcıya "doğrulayın" notuyla sunulmalı.
        var text = """
            PAT.CIPSI PATITO %08 *3,25
            SUT AROMALI %08 *1,00
            ALISVERIS POSETI %18 *0,25
            """;

        var parser = new ReceiptParserService();
        var result = parser.Parse(text);

        result.Fields[ReceiptFieldName.SubTotal].Found.Should().BeTrue();
        result.Fields[ReceiptFieldName.TotalAmount].Found.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleCode == "SUBTOTAL_COMPUTED");
        result.Issues.Should().Contain(i => i.RuleCode == "TOTAL_COMPUTED");
        // Toplam hesaplanabildiği için artık "bulunamadı" hatası verilmemeli.
        result.Issues.Should().NotContain(i => i.RuleCode == "TOTAL_NOT_FOUND");
    }

    [Fact]
    public void Extract_DiscountPercentage_IsNotMisinterpretedAsVatRate()
    {
        // "%25" gibi bilinen KDV oranları dışındaki bir yüzde (ör. indirim) yanlışlıkla
        // KDV grubuna dahil edilmemeli.
        var text = "URUN INDIRIMLI %25 *10,00\nTOPLAM *10,00";

        var context = new ParsedOcrContext(text);
        var result = _sut.Extract(context);

        result.Should().BeEmpty();
    }
}
