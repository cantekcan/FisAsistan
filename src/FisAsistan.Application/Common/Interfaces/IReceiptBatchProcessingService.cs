namespace FisAsistan.Application.Common.Interfaces;

/// <summary>
/// Onaylanmış bir ReceiptBatch'in bekleyen bölgelerini (PendingRegionsJson) işleyip her birini
/// bağımsız bir Receipt'e (mevcut kırpma-önişleme-OCR-parser pipeline'ından geçirerek) dönüştürür.
/// </summary>
public interface IReceiptBatchProcessingService
{
    Task ProcessBatchAsync(Guid batchId, Guid userId, CancellationToken ct = default);
}
