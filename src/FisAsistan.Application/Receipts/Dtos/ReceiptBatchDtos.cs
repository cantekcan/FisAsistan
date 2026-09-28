using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Domain.Enums;

namespace FisAsistan.Application.Receipts.Dtos;

/// <summary>Kullanıcı onayı bekleyen (veya kullanıcının düzenlediği) tek bir bölge.</summary>
public class ReceiptBatchRegionDto
{
    public int Index { get; set; }
    public SegmentPointDto[] Corners { get; set; } = Array.Empty<SegmentPointDto>();

    /// <summary>true ise bu bölge algoritma tarafından değil, kullanıcı tarafından elle eklendi/düzeltildi.</summary>
    public bool IsUserModified { get; set; }
}

public class ReceiptBatchDetailDto
{
    public Guid Id { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public ReceiptBatchStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Message { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    /// <summary>Henüz işlenmemiş, kullanıcı onayı bekleyen bölgeler (işlem sonrası boşalır).</summary>
    public List<ReceiptBatchRegionDto> PendingRegions { get; set; } = new();

    /// <summary>İşlenmiş (Receipt'e dönüşmüş) fişlerin özet listesi.</summary>
    public List<ReceiptListItemDto> Receipts { get; set; } = new();
}

public class UpdateBatchRegionsRequest
{
    /// <summary>Güncel bölge listesinin TAMAMI — istemci bir bölgeyi silmek için onu listeden çıkarır,
    /// yeni bölge eklemek için listeye ekler, mevcut listeyi bütünüyle bu değerle değiştirir.</summary>
    public List<ReceiptBatchRegionDto> Regions { get; set; } = new();
}
