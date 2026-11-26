using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Reports;

/// <summary>Bir dönemin ham verileri. Hem ekrandaki rapor hem Excel dosyası bundan üretilir.</summary>
public sealed record ReportData(DateRange Range, List<CashEntry> Cash, List<Sale> Sales, List<StockMovement> Moves, Dictionary<int, Product> Products)
{
    public List<Sale> ValidSales { get; } = Sales.Where(s => !s.IsCancelled).ToList();
    public decimal Income => Cash.Where(c => c.Type == CashType.Income).Sum(c => c.Amount);
    public decimal Expense => Cash.Where(c => c.Type == CashType.Expense).Sum(c => c.Amount);
}

public sealed class ReportService(AppDb db)
{
    public async Task<ReportData> LoadAsync(DateRange range)
    {
        var (from, to) = range;
        return new ReportData(range,
            await db.CashEntries.AsNoTracking().Where(c => c.Date >= from && c.Date <= to).OrderBy(c => c.Date).ThenBy(c => c.Id).ToListAsync(),
            await db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => s.Date >= from && s.Date <= to).OrderBy(s => s.Date).ThenBy(s => s.Id).ToListAsync(),
            await db.StockMovements.AsNoTracking().Where(m => m.Date >= from && m.Date <= to).OrderBy(m => m.Date).ThenBy(m => m.Id).ToListAsync(),
            await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id));
    }

    public async Task<ReportDto> BuildAsync(DateRange range) => Build(await LoadAsync(range));

    /// <summary>Rapor hesapları. Saf fonksiyon: veritabanına dokunmaz, bu yüzden kolayca test edilebilir.</summary>
    public static ReportDto Build(ReportData d)
    {
        var sales = d.ValidSales;
        var revenue = sales.Sum(s => s.Total);
        var cost = sales.Sum(s => s.CostTotal);

        // KDV tahmini: satışta hesaplanan − alışta/giderde ödenen. Kesin beyan için muhasebeci gerekir.
        var outputVat = sales.Sum(s => s.VatTotal);
        var purchaseVat = d.Moves.Where(m => m.Type == MovementType.StockIn)
            .Sum(m => MoneyMath.VatOf(m.Quantity * m.UnitCost, d.Products.TryGetValue(m.ProductId, out var p) ? p.VatRate : 0));
        var manualExpenseVat = d.Cash
            .Where(c => c.Type == CashType.Expense && c.SaleId == null && c.DebtPaymentId == null && c.StockMovementId == null)
            .Sum(c => c.VatAmount);
        var inputVat = MoneyMath.Round(purchaseVat + manualExpenseVat);

        var waste = d.Moves.Where(m => m.Type == MovementType.Waste).ToList();

        // Satış indirimini kalemlere orantılı dağıtarak ürün başına gerçek ciro.
        var topProducts = sales
            .SelectMany(s => s.Lines.Select(l => (Line: l, Factor: s.Gross > 0 ? s.Total / s.Gross : 0)))
            .GroupBy(x => x.Line.ProductId)
            .Select(g => new TopProduct(
                g.Key,
                g.First().Line.ProductName,
                g.First().Line.Unit,
                g.Sum(x => x.Line.Quantity),
                MoneyMath.Round(g.Sum(x => x.Line.LineTotal * x.Factor)),
                MoneyMath.Round(g.Sum(x => x.Line.LineTotal * x.Factor - x.Line.Quantity * x.Line.UnitCost))))
            .OrderByDescending(x => x.Revenue)
            .Take(10);

        return new ReportDto(
            d.Range.From, d.Range.To,
            new CashTotals(d.Income, d.Expense, d.Income - d.Expense, d.Cash.Count),
            d.Cash.GroupBy(c => new { c.Type, c.Category })
                .Select(g => new CategoryTotal(g.Key.Type, g.Key.Category, g.Sum(c => c.Amount), g.Count()))
                .OrderBy(x => x.Type).ThenByDescending(x => x.Total),
            new SalesSummary(
                sales.Count, d.Sales.Count(s => s.IsCancelled), revenue, sales.Sum(s => s.Discount), cost, revenue - cost,
                revenue > 0 ? Math.Round((revenue - cost) / revenue * 100, 1) : 0,
                sales.Count > 0 ? MoneyMath.Round(revenue / sales.Count) : 0),
            sales.GroupBy(s => s.PaymentType).Select(g => new PaymentTotal(g.Key, g.Count(), g.Sum(s => s.Total))),
            topProducts,
            sales.GroupBy(s => s.CreatedBy).Select(g => new SellerTotal(g.Key, g.Count(), g.Sum(s => s.Total))).OrderByDescending(x => x.Total),
            new VatSummary(outputVat, inputVat, MoneyMath.Round(outputVat - inputVat)),
            new LossSummary(waste.Count, MoneyMath.Round(waste.Sum(m => -m.Quantity * m.UnitCost))),
            d.Cash.GroupBy(c => c.Date).OrderBy(g => g.Key).Select(g => new DailyTotal(
                g.Key,
                g.Where(c => c.Type == CashType.Income).Sum(c => c.Amount),
                g.Where(c => c.Type == CashType.Expense).Sum(c => c.Amount))));
    }
}
