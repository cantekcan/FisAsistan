using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using FisAsistan.Application.Common.Interfaces;
using FisAsistan.Application.Receipts.Dtos;

namespace FisAsistan.Infrastructure.Export;

/// <summary>CSV ve Excel (xlsx) dışa aktarımı — CsvHelper ve ClosedXML, ikisi de ücretsiz/açık kaynak (MIT).</summary>
public class ReceiptExportService : IReceiptExportService
{
    public byte[] ExportToCsv(IEnumerable<ReceiptExportRowDto> rows)
    {
        using var memory = new MemoryStream();
        using (var writer = new StreamWriter(memory, leaveOpen: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.GetCultureInfo("tr-TR"))
               {
                   Delimiter = ";"
               }))
        {
            csv.WriteRecords(rows);
        }

        return memory.ToArray();
    }

    public byte[] ExportToXlsx(IEnumerable<ReceiptExportRowDto> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Fişler");

        string[] headers =
        {
            "Fiş ID", "Satıcı Adı", "Satıcı Adresi", "VKN/TCKN", "Tarih", "Saat", "Belge No",
            "Ara Toplam", "Toplam KDV", "Genel Toplam", "Para Birimi", "Ödeme Yöntemi", "Durum", "Onay Tarihi"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var row = 2;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = r.ReceiptId;
            sheet.Cell(row, 2).Value = r.MerchantName;
            sheet.Cell(row, 3).Value = r.MerchantAddress;
            sheet.Cell(row, 4).Value = r.TaxId;
            sheet.Cell(row, 5).Value = r.ReceiptDate;
            sheet.Cell(row, 6).Value = r.ReceiptTime;
            sheet.Cell(row, 7).Value = r.DocumentNumber;
            sheet.Cell(row, 8).Value = r.SubTotal;
            sheet.Cell(row, 9).Value = r.TotalVat;
            sheet.Cell(row, 10).Value = r.TotalAmount;
            sheet.Cell(row, 11).Value = r.Currency;
            sheet.Cell(row, 12).Value = r.PaymentMethod;
            sheet.Cell(row, 13).Value = r.Status;
            sheet.Cell(row, 14).Value = r.ApprovedAtUtc;
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var memory = new MemoryStream();
        workbook.SaveAs(memory);
        return memory.ToArray();
    }
}
