using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Sales;

/// <summary>
/// Satış ve satış iptali. Bir satış tek işlemde (transaction) şunları yapar:
///   1. Sale + SaleLine kayıtları
///   2. Her kalem için stok düşer ve "Satış" stok hareketi yazılır
///   3. Peşin satışta "Satış" geliri, veresiye satışta müşteriye alacak kaydı oluşur
/// Hepsi tek SaveAuditedAsync çağrısıyla kaydedilir, böylece geri alma da hepsini birlikte geri alır.
/// </summary>
public sealed class SaleService(AppDb db)
{
    public async Task<OpResult> CreateAsync(Actor actor, SaleRequest r)
    {
        // ---- 1. Genel kontroller ----
        if (r.Lines is not { Count: > 0 }) return OpResult.Fail("Sepet boş.");
        var date = r.Date ?? Clock.Today;
        if (!actor.IsOwner && date != Clock.Today) return OpResult.Fail("Çalışanlar sadece bugünün tarihine satış girebilir.");
        var customer = Input.Clean(r.Customer, 60);
        var onCredit = r.PaymentType == PaymentType.OnCredit;
        if (onCredit && customer.Length == 0) return OpResult.Fail("Veresiye satış için müşteri adı girin.");
        if (onCredit && r.DueDate is { } due && due < date) return OpResult.Fail("Vade tarihi satış tarihinden önce olamaz.");

        // ---- 2. Kalemler: aynı ürün iki kez eklendiyse birleştir ----
        var lines = r.Lines
            .GroupBy(l => l.ProductId)
            .Select(g => (ProductId: g.Key, Qty: g.Sum(x => x.Quantity ?? 0), Price: g.Select(x => x.UnitPrice).FirstOrDefault(x => x is not null)))
            .ToList();
        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var sale = new Sale
        {
            Date = date, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name,
            PaymentType = r.PaymentType, Customer = customer, Note = Input.Clean(r.Note, 200),
        };
        decimal vatSum = 0;
        foreach (var (productId, qty, price) in lines)
        {
            if (!products.TryGetValue(productId, out var p) || p.IsArchived) return OpResult.Fail("Sepetteki bir ürün bulunamadı; sayfayı yenileyin.");
            if (qty <= 0) return OpResult.Fail($"{p.Name}: miktar sıfırdan büyük olmalı.");
            if (qty > p.Stock) return OpResult.Fail($"Yetersiz stok: {p.Name} (mevcut {Format.Qty(p.Stock)} {p.Unit}, istenen {Format.Qty(qty)}).");
            var unitPrice = MoneyMath.Round(price ?? p.Price);
            if (unitPrice < 0) return OpResult.Fail($"{p.Name}: fiyat negatif olamaz.");

            var lineTotal = MoneyMath.Round(qty * unitPrice);
            vatSum += MoneyMath.VatOf(lineTotal, p.VatRate);
            sale.Lines.Add(new SaleLine
            {
                ProductId = p.Id, ProductName = p.Name, Unit = p.Unit, Quantity = qty,
                UnitPrice = unitPrice, VatRate = p.VatRate, LineTotal = lineTotal, UnitCost = p.Cost,
            });
        }

        // ---- 3. Toplamlar ----
        sale.Gross = sale.Lines.Sum(l => l.LineTotal);
        sale.Discount = MoneyMath.Round(r.Discount ?? 0);
        if (sale.Discount < 0 || sale.Discount > sale.Gross) return OpResult.Fail("İndirim 0 ile sepet toplamı arasında olmalı.");
        sale.Total = sale.Gross - sale.Discount;
        // İndirim KDV'yi de orantılı olarak düşürür.
        sale.VatTotal = sale.Gross > 0 ? MoneyMath.Round(vatSum * sale.Total / sale.Gross) : 0;
        sale.CostTotal = MoneyMath.Round(sale.Lines.Sum(l => l.Quantity * l.UnitCost));
        db.Sales.Add(sale);

        // ---- 4. Stok düş + hareket yaz ----
        foreach (var line in sale.Lines)
        {
            var p = products[line.ProductId];
            p.Stock -= line.Quantity;
            db.StockMovements.Add(new StockMovement
            {
                ProductId = p.Id, Sale = sale, Type = MovementType.Sale, Quantity = -line.Quantity, StockAfter = p.Stock, UnitCost = p.Cost,
                Date = date, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Note = customer,
            });
        }

        // ---- 5. Para: peşinse gelir, veresiyeyse alacak (nakit esası) ----
        if (onCredit)
            db.Debts.Add(new Debt
            {
                Kind = DebtKind.Receivable, Party = customer, Amount = sale.Total, Date = date, DueDate = r.DueDate ?? date.AddDays(30),
                Note = "Veresiye satış", CreatedAt = DateTime.UtcNow, Sale = sale,
            });
        else if (sale.Total > 0)
            db.CashEntries.Add(new CashEntry
            {
                Type = CashType.Income, Amount = sale.Total, VatAmount = sale.VatTotal, Category = "Satış", Date = date,
                Note = $"{sale.Lines.Count} kalem · {Format.Label(r.PaymentType)}", CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Sale = sale,
            });

        var log = await db.SaveAuditedAsync(actor, "sale.create",
            () => $"Satış {Format.SaleNo(sale.Id)}: {sale.Lines.Count} kalem, {Format.Money(sale.Total)} " +
                  $"({Format.Label(sale.PaymentType)}{(customer.Length > 0 ? " · " + customer : "")})");
        return OpResult.Ok(log, new { saleId = sale.Id, no = Format.SaleNo(sale.Id) });
    }

    /// <summary>
    /// Satış iptali: stok geri gelir. Aynı gün iptalde gelir kaydı silinir; sonraki günlerde
    /// geçmiş günün kasası bozulmasın diye bugüne "Satış iadesi" gideri yazılır.
    /// </summary>
    public async Task<OpResult> CancelAsync(Actor actor, int id, CancelRequest r)
    {
        var sale = await db.Sales.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (sale is null) return OpResult.NotFound("Satış bulunamadı.");
        if (sale.IsCancelled) return OpResult.Fail("Bu satış zaten iptal edilmiş.");
        var debt = await db.Debts.Include(d => d.Payments).FirstOrDefaultAsync(d => d.SaleId == id);
        if (debt is { Payments.Count: > 0 })
            return OpResult.Fail("Bu veresiye satışın tahsilatları var. Önce Borç & Alacak ekranından tahsilatları iptal edin.");

        var today = Clock.Today;
        var reason = Input.Clean(r.Reason, 120);
        var ids = sale.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        foreach (var line in sale.Lines)
        {
            var p = products[line.ProductId];
            p.Stock += line.Quantity;
            db.StockMovements.Add(new StockMovement
            {
                ProductId = p.Id, SaleId = sale.Id, Type = MovementType.SaleCancel, Quantity = line.Quantity, StockAfter = p.Stock,
                UnitCost = line.UnitCost, Date = today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Note = reason,
            });
        }

        var income = await db.CashEntries.FirstOrDefaultAsync(c => c.SaleId == id && c.Type == CashType.Income);
        if (income is not null)
        {
            if (income.Date == today) db.CashEntries.Remove(income);
            else db.CashEntries.Add(new CashEntry
            {
                Type = CashType.Expense, Amount = income.Amount, Category = "Satış iadesi", Date = today,
                Note = $"{Format.SaleNo(sale.Id)} iadesi", CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, SaleId = sale.Id,
            });
        }
        if (debt is not null) db.Debts.Remove(debt);

        sale.IsCancelled = true;
        sale.CancelledAt = DateTime.UtcNow;
        sale.CancelReason = reason;

        var log = await db.SaveAuditedAsync(actor, "sale.cancel",
            () => $"Satış iptal edildi: {Format.SaleNo(sale.Id)} ({Format.Money(sale.Total)}){(reason.Length > 0 ? " — " + reason : "")}");
        return OpResult.Ok(log);
    }

    public async Task<List<SaleDto>> ListAsync(DateRange range, bool showCost)
    {
        var list = await db.Sales.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.Date >= range.From && s.Date <= range.To)
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)
            .Take(3000).ToListAsync();
        return list.Select(s => SaleDto.From(s, showCost)).ToList();
    }

    public async Task<SaleDto?> GetAsync(int id, bool showCost)
    {
        var s = await db.Sales.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        return s is null ? null : SaleDto.From(s, showCost);
    }
}
