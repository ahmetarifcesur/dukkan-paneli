using ClosedXML.Excel;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Endpoints;

public static class ReportEndpoints
{
    private sealed record Data(DateOnly From, DateOnly To, List<CashEntry> Cash, List<Sale> Sales, List<StockMovement> Moves, Dictionary<int, Product> Products);

    private static async Task<Data> LoadAsync(AppDb db, DateOnly? from, DateOnly? to)
    {
        var (f, t) = ApiKit.Range(from, to);
        return new Data(f, t,
            await db.CashEntries.AsNoTracking().Where(c => c.Date >= f && c.Date <= t).OrderBy(c => c.Date).ThenBy(c => c.Id).ToListAsync(),
            await db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => s.Date >= f && s.Date <= t).OrderBy(s => s.Date).ThenBy(s => s.Id).ToListAsync(),
            await db.StockMovements.AsNoTracking().Where(m => m.Date >= f && m.Date <= t).OrderBy(m => m.Date).ThenBy(m => m.Id).ToListAsync(),
            await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id));
    }

    private static object Build(Data d)
    {
        var gelir = d.Cash.Where(c => c.Type == CashType.Gelir).Sum(c => c.Amount);
        var gider = d.Cash.Where(c => c.Type == CashType.Gider).Sum(c => c.Amount);
        var sales = d.Sales.Where(s => !s.IsCancelled).ToList();
        var revenue = sales.Sum(s => s.Total);
        var cost = sales.Sum(s => s.CostTotal);

        // KDV: satışlardan hesaplanan − alışlardan/giderlerden indirilecek (yaklaşık; kesin beyan için muhasebeci).
        var outputVat = sales.Sum(s => s.VatTotal);
        var purchaseVat = d.Moves.Where(m => m.Type == MovementType.Giris)
            .Sum(m => ApiKit.VatOf(m.Quantity * m.UnitCost, d.Products.TryGetValue(m.ProductId, out var p) ? p.VatRate : 0));
        var manualVat = d.Cash.Where(c => c.Type == CashType.Gider && c.SaleId == null && c.DebtPaymentId == null && c.StockMovementId == null).Sum(c => c.VatAmount);

        var fire = d.Moves.Where(m => m.Type == MovementType.Fire).ToList();

        return new
        {
            from = d.From, to = d.To,
            totals = new { gelir, gider, net = gelir - gider, count = d.Cash.Count },
            byCategory = d.Cash.GroupBy(c => new { c.Type, c.Category })
                .Select(g => new { type = g.Key.Type, category = g.Key.Category, total = g.Sum(c => c.Amount), count = g.Count() })
                .OrderBy(x => x.type).ThenByDescending(x => x.total),
            sales = new
            {
                count = sales.Count, cancelled = d.Sales.Count(s => s.IsCancelled), revenue, discount = sales.Sum(s => s.Discount),
                cost, grossProfit = revenue - cost, margin = revenue > 0 ? Math.Round((revenue - cost) / revenue * 100, 1) : 0,
                avgBasket = sales.Count > 0 ? ApiKit.R2(revenue / sales.Count) : 0,
            },
            payments = sales.GroupBy(s => s.PaymentType).Select(g => new { type = g.Key, count = g.Count(), total = g.Sum(s => s.Total) }),
            topProducts = sales.SelectMany(s => s.Lines.Select(l => (l, factor: s.Gross > 0 ? s.Total / s.Gross : 0)))
                .GroupBy(x => x.l.ProductId)
                .Select(g => new
                {
                    productId = g.Key,
                    name = g.First().l.ProductName,
                    unit = g.First().l.Unit,
                    quantity = g.Sum(x => x.l.Quantity),
                    revenue = ApiKit.R2(g.Sum(x => x.l.LineTotal * x.factor)),
                    profit = ApiKit.R2(g.Sum(x => x.l.LineTotal * x.factor - x.l.Quantity * x.l.UnitCost)),
                })
                .OrderByDescending(x => x.revenue).Take(10),
            bySeller = sales.GroupBy(s => s.CreatedBy).Select(g => new { name = g.Key, count = g.Count(), total = g.Sum(s => s.Total) }).OrderByDescending(x => x.total),
            vat = new { output = outputVat, input = ApiKit.R2(purchaseVat + manualVat), payable = ApiKit.R2(outputVat - purchaseVat - manualVat) },
            losses = new { count = fire.Count, value = ApiKit.R2(fire.Sum(m => -m.Quantity * m.UnitCost)) },
            daily = d.Cash.GroupBy(c => c.Date).OrderBy(g => g.Key)
                .Select(g => new { date = g.Key, gelir = g.Where(c => c.Type == CashType.Gelir).Sum(c => c.Amount), gider = g.Where(c => c.Type == CashType.Gider).Sum(c => c.Amount) }),
        };
    }

    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var owner = app.MapGroup("/api/reports").RequireAuthorization("Sahip");

        owner.MapGet("/", async (DateOnly? from, DateOnly? to, AppDb db) => Results.Ok(Build(await LoadAsync(db, from, to))));

        owner.MapGet("/excel", async (DateOnly? from, DateOnly? to, AppDb db) =>
        {
            var d = await LoadAsync(db, from, to);
            var shop = (await CoreEndpoints.SettingsAsync(db)).GetValueOrDefault("shopName", "Dükkan");
            using var wb = new XLWorkbook();
            const string money = "#,##0.00 ₺";

            var sum = wb.AddWorksheet("Özet");
            var gelir = d.Cash.Where(c => c.Type == CashType.Gelir).Sum(c => c.Amount);
            var gider = d.Cash.Where(c => c.Type == CashType.Gider).Sum(c => c.Amount);
            var sales = d.Sales.Where(s => !s.IsCancelled).ToList();
            object[][] rows =
            [
                [shop, ""],
                ["Dönem", $"{d.From:dd.MM.yyyy} – {d.To:dd.MM.yyyy}"],
                ["", ""],
                ["Toplam gelir", gelir],
                ["Toplam gider", gider],
                ["Net kâr / zarar", gelir - gider],
                ["Satış adedi", sales.Count],
                ["Satış cirosu", sales.Sum(s => s.Total)],
                ["Satılan malın maliyeti", sales.Sum(s => s.CostTotal)],
                ["Brüt kâr", sales.Sum(s => s.Total - s.CostTotal)],
                ["Hesaplanan KDV (satış)", sales.Sum(s => s.VatTotal)],
            ];
            for (var i = 0; i < rows.Length; i++)
            {
                sum.Cell(i + 1, 1).Value = XLCellValue.FromObject(rows[i][0]);
                sum.Cell(i + 1, 2).Value = XLCellValue.FromObject(rows[i][1]);
                if (rows[i][1] is decimal) sum.Cell(i + 1, 2).Style.NumberFormat.Format = money;
            }
            sum.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            sum.Columns().AdjustToContents();

            AddTable(wb, "Gelir-Gider", ["Tarih", "Tür", "Kategori", "Açıklama", "Tutar", "KDV", "Kaydeden"],
                d.Cash.Select(c => new object?[] { c.Date.ToDateTime(TimeOnly.MinValue), c.Type == CashType.Gelir ? "Gelir" : "Gider", c.Category, c.Note, c.Type == CashType.Gelir ? c.Amount : -c.Amount, c.VatAmount, c.CreatedBy }), [5, 6]);
            AddTable(wb, "Satışlar", ["Fiş No", "Tarih", "Ödeme", "Müşteri", "Ara toplam", "İndirim", "Toplam", "KDV", "Maliyet", "Durum", "Kasiyer"],
                d.Sales.Select(s => new object?[] { ApiKit.SaleNo(s.Id), s.Date.ToDateTime(TimeOnly.MinValue), SalesEndpoints.PayTr(s.PaymentType), s.Customer, s.Gross, s.Discount, s.Total, s.VatTotal, s.CostTotal, s.IsCancelled ? "İptal" : "", s.CreatedBy }), [5, 6, 7, 8, 9]);
            AddTable(wb, "Satış kalemleri", ["Fiş No", "Tarih", "Ürün", "Miktar", "Birim", "Birim fiyat", "Tutar", "KDV %", "Durum"],
                d.Sales.SelectMany(s => s.Lines.Select(l => new object?[] { ApiKit.SaleNo(s.Id), s.Date.ToDateTime(TimeOnly.MinValue), l.ProductName, l.Quantity, l.Unit, l.UnitPrice, l.LineTotal, l.VatRate, s.IsCancelled ? "İptal" : "" })), [6, 7]);
            AddTable(wb, "Stok hareketleri", ["Tarih", "Ürün", "Tür", "Miktar", "Sonraki stok", "Birim maliyet", "Açıklama", "Kaydeden"],
                d.Moves.Select(m => new object?[] { m.Date.ToDateTime(TimeOnly.MinValue), d.Products.GetValueOrDefault(m.ProductId)?.Name, CatalogEndpoints.Tr(m.Type), m.Quantity, m.StockAfter, m.UnitCost, m.Note, m.CreatedBy }), [6]);
            AddTable(wb, "Ürünler (güncel)", ["Ürün", "Kategori", "Stok", "Birim", "Kapasite", "Kritik", "Alış", "Satış", "KDV %", "Stok değeri"],
                d.Products.Values.Where(p => !p.IsArchived).OrderBy(p => p.Name).Select(p => new object?[] { p.Name, p.Category, p.Stock, p.Unit, p.Capacity, p.MinStock, p.Cost, p.Price, p.VatRate, p.Stock * p.Cost }), [7, 8, 10]);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"rapor-{d.From:yyyy-MM-dd}_{d.To:yyyy-MM-dd}.xlsx");
        });
    }

    private static void AddTable(XLWorkbook wb, string name, string[] headers, IEnumerable<object?[]> rows, int[] moneyCols)
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
        foreach (var c in moneyCols) ws.Column(c).Style.NumberFormat.Format = "#,##0.00 ₺";
        var header = ws.Range(1, 1, 1, headers.Length);
        header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E3F3F1"));
        ws.SheetView.FreezeRows(1);
        if (r > 2) ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(r, 200));
    }
}
