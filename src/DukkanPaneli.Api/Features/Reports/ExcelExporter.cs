using ClosedXML.Excel;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Features.Reports;

/// <summary>Dönem raporunu çok sayfalı bir Excel dosyasına (.xlsx) dönüştürür (ClosedXML kütüphanesiyle).</summary>
public static class ExcelExporter
{
    private const string MoneyFormat = "#,##0.00 ₺";

    public static byte[] Export(ReportData d, string shopName)
    {
        using var wb = new XLWorkbook();
        AddSummary(wb, d, shopName);

        AddTable(wb, "Gelir-Gider", ["Tarih", "Tür", "Kategori", "Açıklama", "Tutar", "KDV", "Kaydeden"],
            d.Cash.Select(c => Row(Day(c.Date), Format.Label(c.Type), c.Category, c.Note,
                c.Type == CashType.Income ? c.Amount : -c.Amount, c.VatAmount, c.CreatedBy)),
            moneyColumns: [5, 6]);

        AddTable(wb, "Satışlar", ["Fiş No", "Tarih", "Ödeme", "Müşteri", "Ara toplam", "İndirim", "Toplam", "KDV", "Maliyet", "Durum", "Kasiyer"],
            d.Sales.Select(s => Row(Format.SaleNo(s.Id), Day(s.Date), Format.Label(s.PaymentType), s.Customer,
                s.Gross, s.Discount, s.Total, s.VatTotal, s.CostTotal, s.IsCancelled ? "İptal" : "", s.CreatedBy)),
            moneyColumns: [5, 6, 7, 8, 9]);

        AddTable(wb, "Satış kalemleri", ["Fiş No", "Tarih", "Ürün", "Miktar", "Birim", "Birim fiyat", "Tutar", "KDV %", "Durum"],
            d.Sales.SelectMany(s => s.Lines.Select(l => Row(Format.SaleNo(s.Id), Day(s.Date), l.ProductName, l.Quantity, l.Unit,
                l.UnitPrice, l.LineTotal, l.VatRate, s.IsCancelled ? "İptal" : ""))),
            moneyColumns: [6, 7]);

        AddTable(wb, "Stok hareketleri", ["Tarih", "Ürün", "Tür", "Miktar", "Sonraki stok", "Birim maliyet", "Açıklama", "Kaydeden"],
            d.Moves.Select(m => Row(Day(m.Date), d.Products.GetValueOrDefault(m.ProductId)?.Name, Format.Label(m.Type),
                m.Quantity, m.StockAfter, m.UnitCost, m.Note, m.CreatedBy)),
            moneyColumns: [6]);

        AddTable(wb, "Ürünler (güncel)", ["Ürün", "Kategori", "Stok", "Birim", "Kapasite", "Kritik", "Alış", "Satış", "KDV %", "Stok değeri"],
            d.Products.Values.Where(p => !p.IsArchived).OrderBy(p => p.Name).Select(p => Row(p.Name, p.Category, p.Stock, p.Unit,
                p.Capacity, p.MinStock, p.Cost, p.Price, p.VatRate, p.Stock * p.Cost)),
            moneyColumns: [7, 8, 10]);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static string FileName(DateRange r) => $"rapor-{r.From:yyyy-MM-dd}_{r.To:yyyy-MM-dd}.xlsx";

    private static void AddSummary(XLWorkbook wb, ReportData d, string shopName)
    {
        var ws = wb.AddWorksheet("Özet");
        var sales = d.ValidSales;
        object[][] rows =
        [
            [shopName, ""],
            ["Dönem", $"{d.Range.From:dd.MM.yyyy} – {d.Range.To:dd.MM.yyyy}"],
            ["", ""],
            ["Toplam gelir", d.Income],
            ["Toplam gider", d.Expense],
            ["Net kâr / zarar", d.Income - d.Expense],
            ["Satış adedi", sales.Count],
            ["Satış cirosu", sales.Sum(s => s.Total)],
            ["Satılan malın maliyeti", sales.Sum(s => s.CostTotal)],
            ["Brüt kâr", sales.Sum(s => s.Total - s.CostTotal)],
            ["Hesaplanan KDV (satış)", sales.Sum(s => s.VatTotal)],
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 1, 1).Value = XLCellValue.FromObject(rows[i][0]);
            ws.Cell(i + 1, 2).Value = XLCellValue.FromObject(rows[i][1]);
            if (rows[i][1] is decimal) ws.Cell(i + 1, 2).Style.NumberFormat.Format = MoneyFormat;
        }
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Columns().AdjustToContents();
    }

    /// <summary>Başlık satırı, filtre, dondurulmuş başlık ve para biçimli sütunlarla bir sayfa ekler.</summary>
    private static void AddTable(XLWorkbook wb, string name, string[] headers, IEnumerable<object?[]> rows, int[] moneyColumns)
    {
        var ws = wb.AddWorksheet(name);
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];

        var r = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                var cell = ws.Cell(r, i + 1);
                cell.Value = XLCellValue.FromObject(row[i]);
                if (row[i] is DateTime) cell.Style.DateFormat.Format = "dd.MM.yyyy";
            }
            r++;
        }

        foreach (var c in moneyColumns) ws.Column(c).Style.NumberFormat.Format = MoneyFormat;
        ws.Range(1, 1, 1, headers.Length).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E3F3F1"));
        ws.SheetView.FreezeRows(1);
        if (r > 2) ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
    }

    private static object?[] Row(params object?[] cells) => cells;
    private static DateTime Day(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);
}
