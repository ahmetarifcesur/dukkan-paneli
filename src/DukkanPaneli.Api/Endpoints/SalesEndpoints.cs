using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Endpoints;

public sealed record SaleLineReq(int ProductId, decimal? Quantity, decimal? UnitPrice);
public sealed record SaleReq(DateOnly? Date, PaymentType PaymentType, string? Customer, DateOnly? DueDate, decimal? Discount, string? Note, List<SaleLineReq>? Lines);
public sealed record CancelReq(string? Reason);

public static class SalesEndpoints
{
    public static string PayTr(PaymentType p) => p switch { PaymentType.Nakit => "Nakit", PaymentType.Kart => "Kart", _ => "Veresiye" };

    public static object Dto(Sale s, bool owner) => new
    {
        s.Id, no = ApiKit.SaleNo(s.Id), s.Date, s.CreatedAt, s.CreatedBy, s.PaymentType, s.Customer, s.Gross, s.Discount, s.Total, s.VatTotal,
        costTotal = owner ? s.CostTotal : (decimal?)null, s.Note, s.IsCancelled, s.CancelledAt, s.CancelReason,
        lines = s.Lines.OrderBy(l => l.Id).Select(l => new { l.Id, l.ProductId, l.ProductName, l.Unit, l.Quantity, l.UnitPrice, l.VatRate, l.LineTotal, vat = ApiKit.VatOf(l.LineTotal, l.VatRate) }),
    };

    public static void MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var all = app.MapGroup("/api/sales").RequireAuthorization();

        all.MapPost("/", async (SaleReq r, HttpContext ctx, AppDb db) =>
        {
            var actor = ApiKit.ActorOf(ctx.User);
            if (r.Lines is null || r.Lines.Count == 0) return ApiKit.Fail("Sepet boş.");
            var date = r.Date ?? ApiKit.Today;
            if (!actor.IsOwner && date != ApiKit.Today) return ApiKit.Fail("Çalışanlar sadece bugünün tarihine satış girebilir.");
            var customer = ApiKit.Clean(r.Customer, 60);
            if (r.PaymentType == PaymentType.Veresiye && customer.Length == 0) return ApiKit.Fail("Veresiye satış için müşteri adı girin.");
            if (r.PaymentType == PaymentType.Veresiye && r.DueDate is { } dd && dd < date) return ApiKit.Fail("Vade tarihi satış tarihinden önce olamaz.");

            // Aynı ürün iki kez eklendiyse birleştir.
            var grouped = r.Lines.GroupBy(l => l.ProductId)
                .Select(g => (ProductId: g.Key, Qty: g.Sum(x => x.Quantity ?? 0), Price: g.Select(x => x.UnitPrice).FirstOrDefault(x => x is not null)))
                .ToList();
            var ids = grouped.Select(g => g.ProductId).ToList();
            var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

            var sale = new Sale { Date = date, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, PaymentType = r.PaymentType, Customer = customer, Note = ApiKit.Clean(r.Note, 200) };
            decimal vatSum = 0;
            foreach (var (pid, qty, price) in grouped)
            {
                if (!products.TryGetValue(pid, out var p) || p.IsArchived) return ApiKit.Fail("Sepetteki bir ürün bulunamadı; sayfayı yenileyin.");
                if (qty <= 0) return ApiKit.Fail($"{p.Name}: miktar sıfırdan büyük olmalı.");
                if (qty > p.Stock) return ApiKit.Fail($"Yetersiz stok: {p.Name} (mevcut {ApiKit.Qty(p.Stock)} {p.Unit}, istenen {ApiKit.Qty(qty)}).");
                var unitPrice = ApiKit.R2(price ?? p.Price);
                if (unitPrice < 0) return ApiKit.Fail($"{p.Name}: fiyat negatif olamaz.");
                var lineTotal = ApiKit.R2(qty * unitPrice);
                vatSum += ApiKit.VatOf(lineTotal, p.VatRate);
                sale.Lines.Add(new SaleLine { ProductId = p.Id, ProductName = p.Name, Unit = p.Unit, Quantity = qty, UnitPrice = unitPrice, VatRate = p.VatRate, LineTotal = lineTotal, UnitCost = p.Cost });
            }
            sale.Gross = sale.Lines.Sum(l => l.LineTotal);
            sale.Discount = ApiKit.R2(r.Discount ?? 0);
            if (sale.Discount < 0 || sale.Discount > sale.Gross) return ApiKit.Fail("İndirim 0 ile sepet toplamı arasında olmalı.");
            sale.Total = sale.Gross - sale.Discount;
            sale.VatTotal = sale.Gross > 0 ? ApiKit.R2(vatSum * sale.Total / sale.Gross) : 0;
            sale.CostTotal = ApiKit.R2(sale.Lines.Sum(l => l.Quantity * l.UnitCost));
            db.Sales.Add(sale);

            foreach (var l in sale.Lines)
            {
                var p = products[l.ProductId];
                p.Stock -= l.Quantity;
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, Sale = sale, Date = date, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Type = MovementType.Satis, Quantity = -l.Quantity, StockAfter = p.Stock, UnitCost = p.Cost, Note = customer });
            }

            if (r.PaymentType == PaymentType.Veresiye)
                db.Debts.Add(new Debt { Kind = DebtKind.Alacak, Party = customer, Amount = sale.Total, Date = date, DueDate = r.DueDate ?? date.AddDays(30), Note = "Veresiye satış", CreatedAt = DateTime.UtcNow, Sale = sale });
            else if (sale.Total > 0)
                db.CashEntries.Add(new CashEntry { Type = CashType.Gelir, Amount = sale.Total, VatAmount = sale.VatTotal, Category = "Satış", Date = date, Note = $"{sale.Lines.Count} kalem · {PayTr(r.PaymentType)}", CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Sale = sale });

            var log = await db.SaveAuditedAsync(actor, "sale.create",
                () => $"Satış {ApiKit.SaleNo(sale.Id)}: {sale.Lines.Count} kalem, {ApiKit.Money(sale.Total)} ({PayTr(sale.PaymentType)}{(customer.Length > 0 ? " · " + customer : "")})");
            return ApiKit.Done(log, new { saleId = sale.Id, no = ApiKit.SaleNo(sale.Id) });
        });

        all.MapGet("/", async (DateOnly? from, DateOnly? to, HttpContext ctx, AppDb db) =>
        {
            var (f, t) = ApiKit.Range(from, to);
            var owner = ApiKit.ActorOf(ctx.User).IsOwner;
            var list = await db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => s.Date >= f && s.Date <= t)
                .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).Take(3000).ToListAsync();
            return Results.Ok(list.Select(s => Dto(s, owner)));
        });

        all.MapGet("/{id:int}", async (int id, HttpContext ctx, AppDb db) =>
        {
            var s = await db.Sales.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
            return s is null ? ApiKit.Fail("Satış bulunamadı.", 404) : Results.Ok(Dto(s, ApiKit.ActorOf(ctx.User).IsOwner));
        });

        // İptal: stok geri gelir, gelir kaydı silinir (aynı gün) ya da iade gideri yazılır (sonraki gün).
        all.MapPost("/{id:int}/cancel", async (int id, CancelReq r, HttpContext ctx, AppDb db) =>
        {
            var s = await db.Sales.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return ApiKit.Fail("Satış bulunamadı.", 404);
            if (s.IsCancelled) return ApiKit.Fail("Bu satış zaten iptal edilmiş.");
            var debt = await db.Debts.Include(d => d.Payments).FirstOrDefaultAsync(d => d.SaleId == id);
            if (debt is { Payments.Count: > 0 }) return ApiKit.Fail("Bu veresiye satışın tahsilatları var. Önce Borç & Alacak ekranından tahsilatları iptal edin.");
            var actor = ApiKit.ActorOf(ctx.User);
            var today = ApiKit.Today;
            var ids = s.Lines.Select(l => l.ProductId).ToList();
            var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            foreach (var l in s.Lines)
            {
                var p = products[l.ProductId];
                p.Stock += l.Quantity;
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, SaleId = s.Id, Date = today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Type = MovementType.SatisIptal, Quantity = l.Quantity, StockAfter = p.Stock, UnitCost = l.UnitCost, Note = ApiKit.Clean(r.Reason, 120) });
            }
            var cash = await db.CashEntries.FirstOrDefaultAsync(c => c.SaleId == id && c.Type == CashType.Gelir);
            if (cash is not null)
            {
                if (cash.Date == today) db.CashEntries.Remove(cash);
                else db.CashEntries.Add(new CashEntry { Type = CashType.Gider, Amount = cash.Amount, Category = "Satış iadesi", Date = today, Note = $"{ApiKit.SaleNo(s.Id)} iadesi", CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, SaleId = s.Id });
            }
            if (debt is not null) db.Debts.Remove(debt);
            s.IsCancelled = true;
            s.CancelledAt = DateTime.UtcNow;
            s.CancelReason = ApiKit.Clean(r.Reason, 120);
            var log = await db.SaveAuditedAsync(actor, "sale.cancel",
                () => $"Satış iptal edildi: {ApiKit.SaleNo(s.Id)} ({ApiKit.Money(s.Total)}){(s.CancelReason.Length > 0 ? " — " + s.CancelReason : "")}");
            return ApiKit.Done(log);
        }).RequireAuthorization("Sahip");
    }
}
