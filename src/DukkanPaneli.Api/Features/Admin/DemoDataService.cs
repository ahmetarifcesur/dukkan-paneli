using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Admin;

/// <summary>
/// Örnek işletme verisi. Son 30 gün gerçek satış/stok hareketi olarak simüle edilir
/// (stoklar hareketlerle birebir tutarlıdır); daha eski aylar günlük kasa toplamı olarak girilir.
/// </summary>
public sealed class DemoDataService(AppDb db)
{
    public async Task ClearAsync()
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.CashEntries.ExecuteDeleteAsync();
        await db.DebtPayments.ExecuteDeleteAsync();
        await db.Debts.ExecuteDeleteAsync();
        await db.StockMovements.ExecuteDeleteAsync();
        await db.SaleLines.ExecuteDeleteAsync();
        await db.Sales.ExecuteDeleteAsync();
        await db.Products.ExecuteDeleteAsync();
        await db.Suppliers.ExecuteDeleteAsync();
        await db.Events.ExecuteDeleteAsync();
        // Silinen kayıtlara dokunan eski işlemler artık geri alınamaz.
        await db.AuditLogs.ExecuteUpdateAsync(s => s.SetProperty(a => a.Undoable, false));
        await tx.CommitAsync();
        db.ChangeTracker.Clear();
    }

    public async Task<AuditLog> LoadAsync(Actor actor)
    {
        await ClearAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var rnd = new Random(20261126);
        int Between(int a, int b) => rnd.Next(a, b + 1);
        var today = Clock.Today;
        DateTime At(DateOnly d, int hour) => d.ToDateTime(new TimeOnly(hour, Between(0, 59))).ToUniversalTime();
        var by = actor.Name;

        var sups = new[]
        {
            new Supplier { Name = "Karadeniz Gıda Toptan", Phone = "0452 123 45 67", Email = "satis@karadenizgida.example", Note = "Her salı teslimat yapıyor" },
            new Supplier { Name = "Fındık Diyarı A.Ş.", Phone = "0452 234 56 78", Note = "30 gün vadeli çalışıyor" },
            new Supplier { Name = "Ordu Ambalaj", Phone = "0532 345 67 89" },
            new Supplier { Name = "Yıldız İçecek Dağıtım", Phone = "0541 456 78 90", Note = "Minimum sipariş 2.000 ₺" },
        };
        db.Suppliers.AddRange(sups);
        await db.SaveChangesAsync();

        var defs = new (string Name, string Cat, string Unit, decimal Cap, decimal Min, decimal Cost, decimal Price, decimal Vat, int Sup)[]
        {
            ("Kavrulmuş fındık", "Kuruyemiş", "kg", 120, 20, 290, 450, 1, 1),
            ("Fındık ezmesi 350 g", "Kahvaltılık", "adet", 80, 15, 85, 145, 1, 1),
            ("Çay 1 kg", "İçecek", "adet", 60, 10, 140, 215, 1, 0),
            ("Süzme bal 850 g", "Kahvaltılık", "adet", 40, 8, 240, 390, 1, 0),
            ("Maden suyu (6'lı)", "İçecek", "paket", 100, 20, 34, 60, 1, 3),
            ("Tereyağı 500 g", "Süt ürünleri", "adet", 30, 6, 170, 255, 1, 0),
            ("Hediyelik kutu", "Ambalaj", "adet", 300, 50, 10, 25, 20, 2),
            ("Fındıklı çikolata", "Atıştırmalık", "adet", 90, 15, 42, 75, 10, 1),
        };
        var products = defs.Select(d => new Product
        {
            Name = d.Name, Category = d.Cat, Unit = d.Unit, Capacity = d.Cap, MinStock = d.Min, Cost = d.Cost, Price = d.Price,
            VatRate = d.Vat, SupplierId = sups[d.Sup].Id, Stock = Math.Round(d.Cap * 0.6m),
        }).ToList();
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        var start = today.AddDays(-30);
        foreach (var p in products)
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, Date = start, CreatedAt = At(start, 8), CreatedBy = by, Type = MovementType.Opening, Quantity = p.Stock, StockAfter = p.Stock, UnitCost = p.Cost, Note = "Açılış stoğu" });

        // Tedarikçi gecikmesi senaryosu: bu iki ürün son iki hafta yenilenmiyor (kritik stok uyarısı oluşsun).
        var delayed = new HashSet<int> { products[1].Id, products[4].Id };
        string[] customers = ["", "", "", "", "Elif H.", "Murat B.", "Kafe Liman"];

        for (var day = start.AddDays(1); day <= today; day = day.AddDays(1))
        {
            if (day.DayOfWeek == DayOfWeek.Tuesday)
            {
                foreach (var p in products.Where(p => p.Stock < p.Capacity * 0.35m && !(delayed.Contains(p.Id) && day > today.AddDays(-15))))
                {
                    var qty = Math.Round(p.Capacity * 0.7m - p.Stock);
                    p.Stock += qty;
                    var mv = new StockMovement { ProductId = p.Id, Date = day, CreatedAt = At(day, 10), CreatedBy = by, Type = MovementType.StockIn, Quantity = qty, StockAfter = p.Stock, UnitCost = p.Cost, Note = sups.First(s => s.Id == p.SupplierId).Name };
                    db.StockMovements.Add(mv);
                    var total = MoneyMath.Round(qty * p.Cost);
                    db.CashEntries.Add(new CashEntry { Type = CashType.Expense, Amount = total, VatAmount = MoneyMath.VatOf(total, p.VatRate), Category = "Tedarik", Date = day, Note = $"{Format.Qty(qty)} {p.Unit} {p.Name}", CreatedAt = mv.CreatedAt, CreatedBy = by, StockMovement = mv });
                }
            }

            var count = day.DayOfWeek switch { DayOfWeek.Saturday => Between(10, 14), DayOfWeek.Sunday => Between(3, 5), _ => Between(6, 10) };
            for (var n = 0; n < count; n++)
            {
                var picks = products.Where(p => p.Stock >= 1).OrderBy(_ => rnd.Next()).Take(Between(1, 3)).ToList();
                if (picks.Count == 0) break;
                var credit = day == today.AddDays(-8) && n == 0;
                var sale = new Sale
                {
                    Date = day, CreatedAt = At(day, Between(9, 19)), CreatedBy = by, CreatedById = actor.Id,
                    PaymentType = credit ? PaymentType.OnCredit : rnd.NextDouble() < 0.65 ? PaymentType.Cash : PaymentType.Card,
                    Customer = credit ? "Mehmet Bey" : customers[Between(0, customers.Length - 1)],
                };
                decimal vat = 0;
                foreach (var p in picks)
                {
                    var qty = p.Unit == "kg" ? Between(1, 4) * 0.5m : Math.Min(p.Stock, Between(1, 3));
                    if (qty > p.Stock) qty = Math.Floor(p.Stock);
                    if (qty <= 0) continue;
                    var lt = MoneyMath.Round(qty * p.Price);
                    vat += MoneyMath.VatOf(lt, p.VatRate);
                    sale.Lines.Add(new SaleLine { ProductId = p.Id, ProductName = p.Name, Unit = p.Unit, Quantity = qty, UnitPrice = p.Price, VatRate = p.VatRate, LineTotal = lt, UnitCost = p.Cost });
                    p.Stock -= qty;
                    db.StockMovements.Add(new StockMovement { ProductId = p.Id, Sale = sale, Date = day, CreatedAt = sale.CreatedAt, CreatedBy = by, Type = MovementType.Sale, Quantity = -qty, StockAfter = p.Stock, UnitCost = p.Cost, Note = sale.Customer });
                }
                if (sale.Lines.Count == 0) continue;
                sale.Gross = sale.Total = sale.Lines.Sum(l => l.LineTotal);
                sale.VatTotal = MoneyMath.Round(vat);
                sale.CostTotal = MoneyMath.Round(sale.Lines.Sum(l => l.Quantity * l.UnitCost));
                db.Sales.Add(sale);
                if (credit)
                    db.Debts.Add(new Debt { Kind = DebtKind.Receivable, Party = sale.Customer, Amount = sale.Total, Date = day, DueDate = day.AddDays(30), Note = "Veresiye satış", CreatedAt = sale.CreatedAt, Sale = sale });
                else
                    db.CashEntries.Add(new CashEntry { Type = CashType.Income, Amount = sale.Total, VatAmount = sale.VatTotal, Category = "Satış", Date = day, Note = $"{sale.Lines.Count} kalem · {(sale.PaymentType == PaymentType.Cash ? "Nakit" : "Kart")}", CreatedAt = sale.CreatedAt, CreatedBy = by, Sale = sale });
            }
        }

        // Daha eski aylar: günlük kasa toplamları ve haftalık mal alımları.
        void Cash(CashType t, decimal amount, string cat, DateOnly d, string note) =>
            db.CashEntries.Add(new CashEntry { Type = t, Amount = amount, Category = cat, Date = d, Note = note, CreatedAt = At(d, 18), CreatedBy = by });
        for (var i = 175; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            if (d <= start)
            {
                if (d.DayOfWeek != DayOfWeek.Sunday || rnd.NextDouble() < 0.4)
                    Cash(CashType.Income, d.DayOfWeek == DayOfWeek.Saturday ? Between(4500, 8500) : Between(1800, 5200), "Satış", d, "Günlük kasa");
                if (d.DayOfWeek == DayOfWeek.Tuesday) Cash(CashType.Expense, Between(4000, 9000), "Tedarik", d, "Haftalık mal alımı");
            }
            if (d.Day == 1) Cash(CashType.Expense, 15000, "Kira", d, "Dükkan kirası");
            if (d.Day == 10) Cash(CashType.Expense, Between(1800, 2800), "Elektrik/Su", d, "Fatura");
            if (d.Day == 15) Cash(CashType.Expense, 18000, "Personel", d, "Maaş");
            if (d.Day == 20 && rnd.NextDouble() < 0.5) Cash(CashType.Expense, Between(800, 2500), "Bakım", d, "Bakım / onarım");
            if (d.Day == 26) Cash(CashType.Expense, Between(3000, 6000), "Vergi", d, "KDV / stopaj");
            if (d.Day == 5 && rnd.NextDouble() < 0.5) Cash(CashType.Income, Between(2000, 6000), "Hizmet", d, "Toptan sipariş hazırlama");
        }

        var now = DateTime.UtcNow;
        db.Debts.AddRange(
            new Debt { Kind = DebtKind.Payable, Party = sups[1].Name, SupplierId = sups[1].Id, Amount = 18500, OpeningPaid = 6000, Date = today.AddDays(-25), DueDate = today.AddDays(5), Note = "Fındık alımı (vadeli)", CreatedAt = now },
            new Debt { Kind = DebtKind.Payable, Party = sups[0].Name, SupplierId = sups[0].Id, Amount = 7400, Date = today.AddDays(-40), DueDate = today.AddDays(-3), Note = "Geçen ayki mal alımı", CreatedAt = now },
            new Debt { Kind = DebtKind.Payable, Party = "Esnaf kredisi (banka)", Amount = 60000, OpeningPaid = 35000, Date = today.AddDays(-200), DueDate = today.AddDays(18), Note = "Aylık taksit", CreatedAt = now },
            new Debt { Kind = DebtKind.Receivable, Party = "Ayşe Hanım", Amount = 1250, OpeningPaid = 400, Date = today.AddDays(-15), DueDate = today.AddDays(2), CreatedAt = now },
            new Debt { Kind = DebtKind.Receivable, Party = "Kafe Liman", Amount = 5600, Date = today.AddDays(-10), DueDate = today.AddDays(12), Note = "Toptan fındık satışı", CreatedAt = now },
            new Debt { Kind = DebtKind.Payable, Party = sups[2].Name, SupplierId = sups[2].Id, Amount = 3200, OpeningPaid = 3200, Date = today.AddDays(-60), DueDate = today.AddDays(-30), Note = "Kutu alımı", CreatedAt = now });

        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        db.Events.AddRange(
            new CalendarEvent { Title = "Raf düzenleme", Date = today, Time = "18:00", Type = "diger" },
            new CalendarEvent { Title = "Fındık Diyarı teslimatı", Date = today.AddDays(1), Time = "10:00", Type = "teslimat", Note = "40 kg fındık" },
            new CalendarEvent { Title = "Muhasebeci ile görüşme", Date = today.AddDays(4), Time = "14:30", Type = "toplanti", Note = "Yıl sonu kapanışı" },
            new CalendarEvent { Title = "Yıldız İçecek siparişi", Date = today.AddDays(-2), Type = "teslimat" },
            new CalendarEvent { Title = "Kira ödemesi", Date = nextMonth, Type = "odeme", Note = "15.000 ₺" },
            new CalendarEvent { Title = "Maaş ödemesi", Date = nextMonth.AddDays(14), Type = "odeme" },
            new CalendarEvent { Title = "Yılbaşı kampanyası başlıyor", Date = nextMonth.AddDays(14), Time = "09:00", Type = "kampanya", Note = "Hediyelik kutularda %15 indirim" });

        await db.SaveChangesAsync();
        var log = new AuditLog { At = DateTime.UtcNow, UserId = actor.Id, UserName = actor.Name, Action = "demo.load", Summary = "Örnek veriler yüklendi (önceki veriler silindi)", Undoable = false };
        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return log;
    }
}
