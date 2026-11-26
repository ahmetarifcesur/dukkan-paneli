using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Reports;

namespace DukkanPaneli.Tests.Unit;

/// <summary>ReportService.Build saf bir fonksiyon olduğu için veritabanı olmadan elle hazırlanan veriyle test edilir.</summary>
public class ReportBuildTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public void Brut_kar_marj_ve_iptal_edilen_satis()
    {
        var sales = new List<Sale>
        {
            NewSale(1, total: 200, cost: 120),
            NewSale(2, total: 100, cost: 60),
            NewSale(3, total: 999, cost: 1, cancelled: true), // iptal: hesaba girmemeli
        };
        var data = new ReportData(new DateRange(Day, Day), Cash: [], sales, Moves: [], Products: []);

        var r = ReportService.Build(data);

        Assert.Equal(2, r.Sales.Count);
        Assert.Equal(1, r.Sales.Cancelled);
        Assert.Equal(300, r.Sales.Revenue);
        Assert.Equal(120, r.Sales.GrossProfit);   // 300 − 180
        Assert.Equal(40, r.Sales.Margin);         // 120 / 300 = %40
        Assert.Equal(150, r.Sales.AvgBasket);
    }

    [Fact]
    public void Gelir_gider_net_ve_kdv_tahmini()
    {
        var cash = new List<CashEntry>
        {
            new() { Type = CashType.Income, Amount = 1200, Category = "Satış", Date = Day },
            new() { Type = CashType.Expense, Amount = 240, VatAmount = 40, Category = "Elektrik", Date = Day }, // elle girilen gider
        };
        var sales = new List<Sale> { NewSale(1, total: 1200, cost: 600, vat: 200) };
        var data = new ReportData(new DateRange(Day, Day), cash, sales, Moves: [], Products: []);

        var r = ReportService.Build(data);

        Assert.Equal(960, r.Totals.Net);
        Assert.Equal(200, r.Vat.Output);
        Assert.Equal(40, r.Vat.Input);
        Assert.Equal(160, r.Vat.Payable);
    }

    private static Sale NewSale(int id, decimal total, decimal cost, decimal vat = 0, bool cancelled = false) => new()
    {
        Id = id, Date = Day, Gross = total, Total = total, CostTotal = cost, VatTotal = vat,
        IsCancelled = cancelled, CreatedBy = "Test", PaymentType = PaymentType.Cash,
    };
}
