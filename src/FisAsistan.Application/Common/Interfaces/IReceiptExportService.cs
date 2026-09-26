using FisAsistan.Application.Receipts.Dtos;

namespace FisAsistan.Application.Common.Interfaces;

public interface IReceiptExportService
{
    byte[] ExportToCsv(IEnumerable<ReceiptExportRowDto> rows);
    byte[] ExportToXlsx(IEnumerable<ReceiptExportRowDto> rows);
}
