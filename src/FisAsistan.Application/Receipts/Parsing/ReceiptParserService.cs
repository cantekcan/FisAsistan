using System.Globalization;
using FisAsistan.Application.Receipts.Parsing.Extractors;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Parsing;

/// <summary>
/// Ham OCR metnini alıp tüm alan çıkarıcılarını (extractor) çalıştıran, sonuçları
/// toplayan ve tutarlılık kontrollerini (ör. ara toplam + KDV = genel toplam) yapan
/// orkestrasyon servisi. Kendisi regex/iş mantığı barındırmaz — her kural kendi sınıfındadır.
/// </summary>
public class ReceiptParserService
{
    private readonly IReadOnlyList<IFieldExtractor> _extractors;
    private readonly IVatLineExtractor _vatLineExtractor;

    private const decimal AmountTolerance = 0.05m;

    public ReceiptParserService(IReadOnlyList<IFieldExtractor>? extractors = null, IVatLineExtractor? vatLineExtractor = null)
    {
        _extractors = extractors ?? DefaultExtractors();
        _vatLineExtractor = vatLineExtractor ?? new VatLineExtractor();
    }

    private static IReadOnlyList<IFieldExtractor> DefaultExtractors() => new List<IFieldExtractor>
    {
        new DateFieldExtractor(),
        new TimeFieldExtractor(),
        new TaxIdFieldExtractor(),
        new DocumentNumberFieldExtractor(),
        new SubTotalFieldExtractor(),
        new TotalAmountFieldExtractor(),
        new CurrencyFieldExtractor(),
        new PaymentMethodFieldExtractor(),
        new MerchantNameFieldExtractor(),
        new MerchantAddressFieldExtractor(),
    };

    public ReceiptParseResult Parse(string rawOcrText)
    {
        var context = new ParsedOcrContext(rawOcrText);
        var result = new ReceiptParseResult();

        foreach (var extractor in _extractors)
        {
            result.Fields[extractor.FieldName] = extractor.Extract(context);
        }

        result.VatLines.AddRange(_vatLineExtractor.Extract(context));

        DeriveTotalsFromVatGroupsIfPossible(result);
        RunConsistencyChecks(result);

        return result;
    }

    /// <summary>
    /// Ürünler KDV oranına göre gruplanıp toplandığında, her grubun matrahı (KDV hariç tutar)
    /// zaten bilinir. Fişte "ARA TOPLAM" ve/veya "GENEL TOPLAM" satırları OCR ile okunamadıysa
    /// (kırışık/eğik fotoğraf gibi durumlarda sık görülür), bu bilinen matrah ve KDV tutarlarından
    /// hesaplanarak boş bırakmak yerine bir değer önerilir — ama her zaman açıkça "hesaplandı"
    /// olarak işaretlenip kullanıcıya doğrulatılır, gerçek bir OCR bulgusu gibi sunulmaz.
    /// </summary>
    private void DeriveTotalsFromVatGroupsIfPossible(ReceiptParseResult result)
    {
        if (result.VatLines.Count == 0 || result.VatLines.Any(v => !v.BaseAmount.HasValue))
        {
            // Yalnızca ürün gruplama yönteminden gelen KDV satırlarının matrahı bilinir;
            // fişte doğrudan etiketlenmiş dip toplamlarda matrah genelde yazmadığından
            // güvenilir bir hesaplama yapılamaz.
            return;
        }

        var computedSubTotal = result.VatLines.Sum(v => v.BaseAmount!.Value);

        if (!result.Fields[ReceiptFieldName.SubTotal].Found)
        {
            result.Fields[ReceiptFieldName.SubTotal] = ExtractionCandidate.Success(
                computedSubTotal.ToString("0.00", CultureInfo.InvariantCulture),
                "Fişte 'ARA TOPLAM' etiketi bulunamadı; KDV oranına göre gruplanan ürünlerin matrahları " +
                "toplanarak hesaplandı — lütfen doğrulayın.");

            result.Issues.Add(new ValidationIssueCandidate
            {
                FieldName = ReceiptFieldName.SubTotal,
                Severity = IssueSeverity.Info,
                RuleCode = "SUBTOTAL_COMPUTED",
                Message = "Ara toplam fişte doğrudan yazmadığı için KDV grup matrahlarından hesaplandı, lütfen doğrulayın."
            });
        }

        if (!result.Fields[ReceiptFieldName.TotalAmount].Found)
        {
            var vatSum = result.VatLines.Sum(v => v.VatAmount);
            var currentSubTotal = TurkishNumberNormalizer.TryParse(result.Fields[ReceiptFieldName.SubTotal].Value)
                                   ?? computedSubTotal;
            var computedTotal = currentSubTotal + vatSum;

            result.Fields[ReceiptFieldName.TotalAmount] = ExtractionCandidate.Success(
                computedTotal.ToString("0.00", CultureInfo.InvariantCulture),
                "Genel toplam fişten okunamadı; ara toplam (bulunan veya hesaplanan) + KDV toplamı " +
                "üzerinden hesaplandı — mutlaka fişle karşılaştırıp doğrulayın.");

            result.Issues.Add(new ValidationIssueCandidate
            {
                FieldName = ReceiptFieldName.TotalAmount,
                Severity = IssueSeverity.Warning,
                RuleCode = "TOTAL_COMPUTED",
                Message = "Genel toplam fişte okunamadığı için ara toplam + KDV toplamından hesaplandı. " +
                          "Muhasebe kaydından önce mutlaka fişle karşılaştırıp doğrulayın."
            });
        }
    }

    private void RunConsistencyChecks(ReceiptParseResult result)
    {
        if (!result.Fields.TryGetValue(ReceiptFieldName.ReceiptDate, out var dateField) || !dateField.Found)
        {
            result.Issues.Add(new ValidationIssueCandidate
            {
                FieldName = ReceiptFieldName.ReceiptDate,
                Severity = IssueSeverity.Warning,
                RuleCode = "DATE_NOT_FOUND",
                Message = "Fiş tarihi otomatik olarak tespit edilemedi. Lütfen manuel giriniz."
            });
        }

        var hasTotal = result.Fields.TryGetValue(ReceiptFieldName.TotalAmount, out var totalField) && totalField.Found;
        if (!hasTotal)
        {
            result.Issues.Add(new ValidationIssueCandidate
            {
                FieldName = ReceiptFieldName.TotalAmount,
                Severity = IssueSeverity.Error,
                RuleCode = "TOTAL_NOT_FOUND",
                Message = "Genel toplam tutarı tespit edilemedi. Muhasebe kaydı için bu alan zorunludur, lütfen manuel giriniz."
            });
        }

        var hasTaxId = result.Fields.TryGetValue(ReceiptFieldName.TaxId, out var taxIdField) && taxIdField.Found;
        if (!hasTaxId)
        {
            result.Issues.Add(new ValidationIssueCandidate
            {
                FieldName = ReceiptFieldName.TaxId,
                Severity = IssueSeverity.Warning,
                RuleCode = "TAXID_NOT_FOUND",
                Message = "VKN/TCKN tespit edilemedi. Lütfen fişi kontrol edip manuel giriniz."
            });
        }

        decimal? totalAmount = hasTotal ? TurkishNumberNormalizer.TryParse(totalField!.Value) : null;
        decimal? subTotal = result.Fields.TryGetValue(ReceiptFieldName.SubTotal, out var subField) && subField.Found
            ? TurkishNumberNormalizer.TryParse(subField.Value)
            : null;
        var vatSum = result.VatLines.Count > 0 ? result.VatLines.Sum(v => v.VatAmount) : (decimal?)null;

        if (subTotal.HasValue && vatSum.HasValue && totalAmount.HasValue)
        {
            var expected = subTotal.Value + vatSum.Value;
            if (Math.Abs(expected - totalAmount.Value) > AmountTolerance)
            {
                result.Issues.Add(new ValidationIssueCandidate
                {
                    FieldName = ReceiptFieldName.TotalAmount,
                    Severity = IssueSeverity.Warning,
                    RuleCode = "TOTAL_MISMATCH",
                    Message = $"Tutarsızlık: Ara Toplam ({subTotal.Value}) + KDV toplamı ({vatSum.Value}) = {expected}, " +
                              $"ancak Genel Toplam {totalAmount.Value} olarak bulundu. Lütfen kontrol ediniz."
                });
            }
        }
        else if (result.VatLines.Count == 0 && hasTotal)
        {
            result.Issues.Add(new ValidationIssueCandidate
            {
                Severity = IssueSeverity.Info,
                RuleCode = "VAT_NOT_FOUND",
                Message = "Metinde KDV satırı tespit edilemedi. Fişte KDV dökümü yoksa bu normaldir, aksi halde manuel giriniz."
            });
        }

        if (hasTaxId && taxIdField!.Value?.Length == 11 && !TaxIdFieldExtractor.IsValidTckn(taxIdField.Value))
        {
            result.Issues.Add(new ValidationIssueCandidate
            {
                FieldName = ReceiptFieldName.TaxId,
                Severity = IssueSeverity.Warning,
                RuleCode = "TCKN_CHECKSUM_INVALID",
                Message = "11 haneli numara TCKN algoritma kontrolünden geçemedi; OCR hatası olabilir, lütfen doğrulayın."
            });
        }
    }
}
