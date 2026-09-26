using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Dtos;

public class ReceiptFieldDto
{
    public ReceiptFieldName FieldName { get; set; }
    public string? ParserValue { get; set; }
    public string? CurrentValue { get; set; }
    public ValueSource Source { get; set; }
    public string? ParserExplanation { get; set; }
    public bool IsConfirmed { get; set; }
}

public class ReceiptVatLineDto
{
    public Guid Id { get; set; }
    public decimal RatePercent { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal VatAmount { get; set; }
    public ValueSource Source { get; set; }
}

public class ReceiptValidationIssueDto
{
    public ReceiptFieldName? FieldName { get; set; }
    public IssueSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
}

public class ReceiptListItemDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public ReceiptStatus Status { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public string? MerchantName { get; set; }
    public string? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public int OpenIssueCount { get; set; }
}

public class ReceiptDetailDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public ReceiptStatus Status { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? RawOcrText { get; set; }
    public double? OcrAverageConfidence { get; set; }
    public string? RejectionReason { get; set; }

    public List<ReceiptFieldDto> Fields { get; set; } = new();
    public List<ReceiptVatLineDto> VatLines { get; set; } = new();
    public List<ReceiptValidationIssueDto> ValidationIssues { get; set; } = new();
}

/// <summary>Kullanıcının form üzerinden düzelttiği alanları API'ye gönderirken kullandığı istek gövdesi.</summary>
public class UpdateReceiptFieldsRequest
{
    public List<UpdateReceiptFieldItem> Fields { get; set; } = new();
    public List<UpdateVatLineItem> VatLines { get; set; } = new();
}

public class UpdateReceiptFieldItem
{
    public ReceiptFieldName FieldName { get; set; }
    public string? Value { get; set; }
    public bool IsConfirmed { get; set; }
}

public class UpdateVatLineItem
{
    public Guid? Id { get; set; }
    public decimal RatePercent { get; set; }
    public decimal? BaseAmount { get; set; }
    public decimal VatAmount { get; set; }
}

public class RejectReceiptRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class ReceiptExportRowDto
{
    public string ReceiptId { get; set; } = string.Empty;
    public string MerchantName { get; set; } = string.Empty;
    public string MerchantAddress { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string ReceiptDate { get; set; } = string.Empty;
    public string ReceiptTime { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string SubTotal { get; set; } = string.Empty;
    public string TotalVat { get; set; } = string.Empty;
    public string TotalAmount { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ApprovedAtUtc { get; set; } = string.Empty;
}
