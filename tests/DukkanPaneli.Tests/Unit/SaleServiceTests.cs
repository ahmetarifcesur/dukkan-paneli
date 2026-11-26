using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Tests.Unit;

/// <summary>
/// Servisi HTTP olmadan, doğrudan çağırarak test etmek. Veritabanı bellekte (":memory:") tutulur;
/// bağlantı açık kaldıkça veritabanı yaşar, test bitince kaybolur.
/// </summary>
public sealed class SaleServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDb _db;
    private readonly SaleService _sales;
    private static readonly Actor Owner = new(1, "Sahip", Role.Owner);
    private static readonly Actor Staff = new(2, "Kasa", Role.Staff);

    public SaleServiceTests()
    {
        _connection.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        // Satış, yapan kullanıcıya numarasıyla bağlanır (Sale.CreatedById); kullanıcılar gerçekten var olmalı.
        _db.Users.AddRange(
            new User { Id = Owner.Id!.Value, UserName = "sahip", DisplayName = Owner.Name, Role = Role.Owner },
            new User { Id = Staff.Id!.Value, UserName = "kasa", DisplayName = Staff.Name, Role = Role.Staff });
        _db.SaveChanges();
        _sales = new SaleService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Product> AddProduct(decimal stock = 10, decimal price = 100, decimal vat = 20)
    {
        var p = new Product { Name = "Çay", Stock = stock, Capacity = 50, Price = price, Cost = 60, VatRate = vat };
        _db.Products.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private static SaleRequest Request(int productId, decimal qty, decimal? discount = null, PaymentType pay = PaymentType.Cash, string? customer = null, DateOnly? date = null) =>
        new(date, pay, customer, null, discount, null, [new SaleLineRequest(productId, qty, null)]);

    [Fact]
    public async Task Indirim_kdvyi_orantili_dusurur()
    {
        var p = await AddProduct(price: 120, vat: 20);
        var result = await _sales.CreateAsync(Owner, Request(p.Id, 1, discount: 60)); // 120 → 60
        Assert.True(result.Succeeded, result.Error);

        var sale = await _db.Sales.SingleAsync();
        Assert.Equal(60, sale.Total);
        Assert.Equal(10, sale.VatTotal); // tam fiyatta 20 olurdu; yarı fiyat → yarı KDV
    }

    [Fact]
    public async Task Tek_islemde_satis_hareket_ve_gelir_yazar()
    {
        var p = await AddProduct(stock: 10);
        await _sales.CreateAsync(Owner, Request(p.Id, 4));

        Assert.Equal(6, (await _db.Products.SingleAsync()).Stock);
        var move = await _db.StockMovements.SingleAsync();
        Assert.Equal(MovementType.Sale, move.Type);
        Assert.Equal(-4, move.Quantity);
        Assert.Equal(6, move.StockAfter);
        Assert.Equal(CashType.Income, (await _db.CashEntries.SingleAsync()).Type);
        Assert.Single(await _db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Veresiyede_gelir_yerine_alacak_olusur()
    {
        var p = await AddProduct();
        await _sales.CreateAsync(Owner, Request(p.Id, 1, pay: PaymentType.OnCredit, customer: "Ayşe"));

        Assert.Empty(await _db.CashEntries.ToListAsync());
        var debt = await _db.Debts.SingleAsync();
        Assert.Equal(DebtKind.Receivable, debt.Kind);
        Assert.Equal("Ayşe", debt.Party);
    }

    [Fact]
    public async Task Hatali_istekte_hicbir_sey_kaydedilmez()
    {
        var p = await AddProduct(stock: 2);
        var result = await _sales.CreateAsync(Owner, Request(p.Id, 5));

        Assert.False(result.Succeeded);
        Assert.Contains("Yetersiz stok", result.Error);
        Assert.Empty(await _db.Sales.ToListAsync());
        Assert.Equal(2, (await _db.Products.AsNoTracking().SingleAsync()).Stock);
    }

    [Fact]
    public async Task Calisan_gecmis_tarihe_satis_yapamaz()
    {
        var p = await AddProduct();
        var result = await _sales.CreateAsync(Staff, Request(p.Id, 1, date: Clock.Today.AddDays(-1)));
        Assert.False(result.Succeeded);

        var ok = await _sales.CreateAsync(Owner, Request(p.Id, 1, date: Clock.Today.AddDays(-1)));
        Assert.True(ok.Succeeded, ok.Error);
    }
}
