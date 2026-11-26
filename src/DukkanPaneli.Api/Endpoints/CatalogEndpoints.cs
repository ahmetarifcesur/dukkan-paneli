using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Endpoints;

public sealed record ProductReq(string? Name, string? Category, string? Unit, decimal? Stock, decimal? Capacity, decimal? MinStock,
    decimal? Cost, decimal? Price, decimal? VatRate, int? SupplierId);
public sealed record StockInReq(decimal? Quantity, decimal? UnitCost, int? SupplierId, DateOnly? Date, bool Credit, DateOnly? DueDate, string? Note);
public sealed record AdjustReq(MovementType Type, decimal? Quantity, string? Note, DateOnly? Date);
public sealed record SupplierReq(string? Name, string? Phone, string? Email, string? Note);

public static class CatalogEndpoints
{
    public static readonly string[] MovementTr = ["Açılış", "Giriş", "Satış", "Satış iptali", "Fire / zayi", "Tedarikçiye iade", "Sayım", "Düzeltme"];
    public static string Tr(MovementType t) => MovementTr[(int)t];

    private static string? Validate(ProductReq r, bool creating)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Ürün adı gerekli.";
        if (creating && (r.Stock is null || r.Stock < 0)) return "Stok 0 veya daha büyük olmalı.";
        if (r.Capacity is null || r.Capacity <= 0) return "Kapasite sıfırdan büyük olmalı.";
        if (r.MinStock is null || r.MinStock < 0) return "Kritik stok 0 veya daha büyük olmalı.";
        if (r.MinStock > r.Capacity) return "Kritik stok seviyesi kapasiteden büyük olamaz.";
        if (r.Cost is null || r.Cost < 0) return "Alış fiyatı 0 veya daha büyük olmalı.";
        if (r.Price is null || r.Price < 0) return "Satış fiyatı 0 veya daha büyük olmalı.";
        if (r.VatRate is null || r.VatRate < 0 || r.VatRate > 100) return "KDV oranı 0-100 arasında olmalı.";
        if (creating && r.Stock > r.Capacity) return $"Stok ({ApiKit.Qty(r.Stock!.Value)}) kapasiteden ({ApiKit.Qty(r.Capacity.Value)}) büyük olamaz.";
        return null;
    }

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var all = app.MapGroup("/api").RequireAuthorization();
        var owner = app.MapGroup("/api").RequireAuthorization("Sahip");

        /* ---------------- Ürünler ---------------- */
        owner.MapPost("/products", async (ProductReq r, HttpContext ctx, AppDb db) =>
        {
            if (Validate(r, true) is { } err) return ApiKit.Fail(err);
            var name = ApiKit.Clean(r.Name, 80);
            if (await db.Products.AnyAsync(p => !p.IsArchived && p.Name.ToLower() == name.ToLower())) return ApiKit.Fail("Bu isimde bir ürün zaten var.");
            if (r.SupplierId is int sid && !await db.Suppliers.AnyAsync(s => s.Id == sid && !s.IsArchived)) return ApiKit.Fail("Tedarikçi bulunamadı.");
            var actor = ApiKit.ActorOf(ctx.User);
            var p = new Product
            {
                Name = name, Category = ApiKit.Clean(r.Category, 40), Unit = ApiKit.Clean(r.Unit, 15) is { Length: > 0 } u ? u : "adet",
                Stock = r.Stock!.Value, Capacity = r.Capacity!.Value, MinStock = r.MinStock!.Value,
                Cost = ApiKit.R2(r.Cost!.Value), Price = ApiKit.R2(r.Price!.Value), VatRate = r.VatRate!.Value, SupplierId = r.SupplierId,
            };
            db.Products.Add(p);
            if (p.Stock > 0)
                db.StockMovements.Add(new StockMovement { Product = p, Date = ApiKit.Today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Type = MovementType.Acilis, Quantity = p.Stock, StockAfter = p.Stock, UnitCost = p.Cost, Note = "Açılış stoğu" });
            var log = await db.SaveAuditedAsync(actor, "product.create", () => $"Ürün eklendi: {p.Name} ({ApiKit.Qty(p.Stock)} {p.Unit})");
            return ApiKit.Done(log, new { p.Id });
        });

        owner.MapPut("/products/{id:int}", async (int id, ProductReq r, HttpContext ctx, AppDb db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null || p.IsArchived) return ApiKit.Fail("Ürün bulunamadı.", 404);
            if (Validate(r, false) is { } err) return ApiKit.Fail(err);
            var name = ApiKit.Clean(r.Name, 80);
            if (await db.Products.AnyAsync(x => x.Id != id && !x.IsArchived && x.Name.ToLower() == name.ToLower())) return ApiKit.Fail("Bu isimde bir ürün zaten var.");
            if (r.Capacity < p.Stock) return ApiKit.Fail($"Kapasite mevcut stoktan ({ApiKit.Qty(p.Stock)}) küçük olamaz.");
            if (r.SupplierId is int sid && !await db.Suppliers.AnyAsync(s => s.Id == sid && !s.IsArchived)) return ApiKit.Fail("Tedarikçi bulunamadı.");
            var oldPrice = p.Price;
            p.Name = name; p.Category = ApiKit.Clean(r.Category, 40); p.Unit = ApiKit.Clean(r.Unit, 15) is { Length: > 0 } u ? u : p.Unit;
            p.Capacity = r.Capacity!.Value; p.MinStock = r.MinStock!.Value; p.Cost = ApiKit.R2(r.Cost!.Value); p.Price = ApiKit.R2(r.Price!.Value);
            p.VatRate = r.VatRate!.Value; p.SupplierId = r.SupplierId;
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "product.update",
                () => $"Ürün güncellendi: {p.Name}" + (oldPrice != p.Price ? $" (fiyat {ApiKit.Money(oldPrice)} → {ApiKit.Money(p.Price)})" : ""));
            return ApiKit.Done(log);
        });

        // Silme yerine arşivleme: geçmiş satışlar ve hareketler ürüne bağlı kalır.
        owner.MapDelete("/products/{id:int}", async (int id, HttpContext ctx, AppDb db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null || p.IsArchived) return ApiKit.Fail("Ürün bulunamadı.", 404);
            p.IsArchived = true;
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "product.archive", () => $"Ürün silindi (arşivlendi): {p.Name}");
            return ApiKit.Done(log);
        });

        all.MapPost("/products/{id:int}/stock-in", async (int id, StockInReq r, HttpContext ctx, AppDb db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null || p.IsArchived) return ApiKit.Fail("Ürün bulunamadı.", 404);
            if (r.Quantity is not > 0) return ApiKit.Fail("Miktar sıfırdan büyük olmalı.");
            var free = p.Capacity - p.Stock;
            if (r.Quantity > free) return ApiKit.Fail($"Kapasite aşılıyor: en fazla {ApiKit.Qty(Math.Max(0, free))} {p.Unit} eklenebilir.");
            var actor = ApiKit.ActorOf(ctx.User);
            var unitCost = r.UnitCost ?? p.Cost; // çalışan maliyeti görmediği için boş gönderebilir
            if (unitCost < 0) return ApiKit.Fail("Alış fiyatı negatif olamaz.");
            Supplier? sup = null;
            if (r.SupplierId is int sid && (sup = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == sid && !s.IsArchived)) is null) return ApiKit.Fail("Tedarikçi bulunamadı.");
            if (r.Credit && sup is null) return ApiKit.Fail("Vadeli alım için tedarikçi seçin.");
            var date = r.Date ?? ApiKit.Today;
            if (r.Credit && r.DueDate is { } due && due < date) return ApiKit.Fail("Vade tarihi alım tarihinden önce olamaz.");

            var qty = r.Quantity.Value;
            var total = ApiKit.R2(qty * unitCost);
            p.Stock += qty;
            p.Cost = ApiKit.R2(unitCost);
            if (sup is not null) p.SupplierId = sup.Id;
            var note = $"{ApiKit.Qty(qty)} {p.Unit} {p.Name}";
            var mv = new StockMovement { ProductId = p.Id, Date = date, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Type = MovementType.Giris, Quantity = qty, StockAfter = p.Stock, UnitCost = p.Cost, Note = ApiKit.Clean(r.Note, 120) is { Length: > 0 } n ? n : (sup?.Name ?? "") };
            db.StockMovements.Add(mv);
            if (r.Credit)
                db.Debts.Add(new Debt { Kind = DebtKind.Borc, Party = sup!.Name, SupplierId = sup.Id, Amount = total, Date = date, DueDate = r.DueDate ?? date.AddDays(30), Note = $"Vadeli alım: {note}", CreatedAt = DateTime.UtcNow, StockMovement = mv });
            else
                db.CashEntries.Add(new CashEntry { Type = CashType.Gider, Amount = total, VatAmount = ApiKit.VatOf(total, p.VatRate), Category = "Tedarik", Date = date, Note = note + (sup is null ? "" : $" — {sup.Name}"), CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, StockMovement = mv });
            var log = await db.SaveAuditedAsync(actor, "stock.in", () => $"Stok girişi: {note}, {ApiKit.Money(total)}{(r.Credit ? " (vadeli)" : "")}");
            return ApiKit.Done(log);
        });

        // Fire, tedarikçiye iade, sayım ve düzeltme. Stok sadece hareketlerle değişir.
        all.MapPost("/products/{id:int}/adjust", async (int id, AdjustReq r, HttpContext ctx, AppDb db) =>
        {
            var p = await db.Products.FindAsync(id);
            if (p is null || p.IsArchived) return ApiKit.Fail("Ürün bulunamadı.", 404);
            if (r.Quantity is null) return ApiKit.Fail("Miktar gerekli.");
            var q = r.Quantity.Value;
            decimal delta = r.Type switch
            {
                MovementType.Fire or MovementType.Iade => -Math.Abs(q),
                MovementType.Sayim => q - p.Stock,
                MovementType.Duzeltme => q,
                _ => decimal.MinValue,
            };
            if (delta == decimal.MinValue) return ApiKit.Fail("Geçersiz hareket türü.");
            if (r.Type is MovementType.Fire or MovementType.Iade && q <= 0) return ApiKit.Fail("Miktar sıfırdan büyük olmalı.");
            if (r.Type == MovementType.Sayim && q < 0) return ApiKit.Fail("Sayılan miktar negatif olamaz.");
            if (delta == 0) return ApiKit.Fail(r.Type == MovementType.Sayim ? "Sayım sonucu mevcut stokla aynı; değişiklik yok." : "Miktar sıfır olamaz.");
            var newStock = p.Stock + delta;
            if (newStock < 0) return ApiKit.Fail($"Stok eksiye düşemez (mevcut {ApiKit.Qty(p.Stock)} {p.Unit}).");
            if (newStock > p.Capacity) return ApiKit.Fail($"Stok kapasiteyi ({ApiKit.Qty(p.Capacity)}) aşamaz. Önce ürünün kapasitesini artırın.");
            if (string.IsNullOrWhiteSpace(r.Note) && r.Type == MovementType.Duzeltme) return ApiKit.Fail("Düzeltme için açıklama yazın (neden düzeltildiği geçmişte görünür).");
            var actor = ApiKit.ActorOf(ctx.User);
            p.Stock = newStock;
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, Date = r.Date ?? ApiKit.Today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Type = r.Type, Quantity = delta, StockAfter = newStock, UnitCost = p.Cost, Note = ApiKit.Clean(r.Note, 120) });
            var log = await db.SaveAuditedAsync(actor, "stock.adjust",
                () => $"{Tr(r.Type)}: {p.Name} {(delta > 0 ? "+" : "")}{ApiKit.Qty(delta)} {p.Unit} (yeni stok {ApiKit.Qty(newStock)})");
            return ApiKit.Done(log);
        });

        // Ürünün hareket geçmişi + istenen tarihteki stok (geçmişe dönük kontrol).
        all.MapGet("/products/{id:int}/movements", async (int id, DateOnly? at, HttpContext ctx, AppDb db) =>
        {
            var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return ApiKit.Fail("Ürün bulunamadı.", 404);
            var list = await db.StockMovements.AsNoTracking().Where(m => m.ProductId == id)
                .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToListAsync();
            decimal? stockAt = at is { } d ? p.Stock - list.Where(m => m.Date > d).Sum(m => m.Quantity) : null;
            var owner = ApiKit.ActorOf(ctx.User).IsOwner;
            return Results.Ok(new
            {
                product = new { p.Id, p.Name, p.Unit, p.Stock },
                stockAt, at,
                movements = list.Take(500).Select(m => new { m.Id, m.Date, m.CreatedAt, m.CreatedBy, m.Type, typeLabel = Tr(m.Type), m.Quantity, m.StockAfter, unitCost = owner ? m.UnitCost : (decimal?)null, m.Note, m.SaleId, saleNo = m.SaleId is int s ? ApiKit.SaleNo(s) : null }),
            });
        });

        // Tüm ürünlerin hareketleri (dönem filtresiyle).
        all.MapGet("/movements", async (DateOnly? from, DateOnly? to, MovementType? type, AppDb db) =>
        {
            var (f, t) = ApiKit.Range(from, to);
            var q = db.StockMovements.AsNoTracking().Where(m => m.Date >= f && m.Date <= t);
            if (type is { } ty) q = q.Where(m => m.Type == ty);
            var names = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Unit });
            var list = await q.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).Take(2000).ToListAsync();
            return Results.Ok(list.Select(m => new { m.Id, m.Date, m.CreatedAt, m.CreatedBy, m.ProductId, product = names.GetValueOrDefault(m.ProductId)?.Name, unit = names.GetValueOrDefault(m.ProductId)?.Unit, m.Type, typeLabel = Tr(m.Type), m.Quantity, m.StockAfter, m.Note, saleNo = m.SaleId is int s ? ApiKit.SaleNo(s) : null }));
        });

        /* ---------------- Tedarikçiler ---------------- */
        owner.MapPost("/suppliers", async (SupplierReq r, HttpContext ctx, AppDb db) =>
        {
            var name = ApiKit.Clean(r.Name, 80);
            if (name.Length == 0) return ApiKit.Fail("Firma adı gerekli.");
            if (await db.Suppliers.AnyAsync(s => !s.IsArchived && s.Name.ToLower() == name.ToLower())) return ApiKit.Fail("Bu isimde bir tedarikçi zaten var.");
            var s = new Supplier { Name = name, Phone = ApiKit.Clean(r.Phone, 25), Email = ApiKit.Clean(r.Email, 80), Note = ApiKit.Clean(r.Note, 200) };
            db.Suppliers.Add(s);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "supplier.create", () => $"Tedarikçi eklendi: {s.Name}");
            return ApiKit.Done(log, new { s.Id });
        });

        owner.MapPut("/suppliers/{id:int}", async (int id, SupplierReq r, HttpContext ctx, AppDb db) =>
        {
            var s = await db.Suppliers.FindAsync(id);
            if (s is null || s.IsArchived) return ApiKit.Fail("Tedarikçi bulunamadı.", 404);
            var name = ApiKit.Clean(r.Name, 80);
            if (name.Length == 0) return ApiKit.Fail("Firma adı gerekli.");
            if (await db.Suppliers.AnyAsync(x => x.Id != id && !x.IsArchived && x.Name.ToLower() == name.ToLower())) return ApiKit.Fail("Bu isimde bir tedarikçi zaten var.");
            if (name != s.Name)
                foreach (var d in await db.Debts.Where(d => d.SupplierId == id).ToListAsync()) d.Party = name;
            s.Name = name; s.Phone = ApiKit.Clean(r.Phone, 25); s.Email = ApiKit.Clean(r.Email, 80); s.Note = ApiKit.Clean(r.Note, 200);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "supplier.update", () => $"Tedarikçi güncellendi: {s.Name}");
            return ApiKit.Done(log);
        });

        owner.MapDelete("/suppliers/{id:int}", async (int id, HttpContext ctx, AppDb db) =>
        {
            var s = await db.Suppliers.FindAsync(id);
            if (s is null || s.IsArchived) return ApiKit.Fail("Tedarikçi bulunamadı.", 404);
            s.IsArchived = true;
            foreach (var p in await db.Products.Where(p => p.SupplierId == id).ToListAsync()) p.SupplierId = null;
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "supplier.archive", () => $"Tedarikçi silindi: {s.Name}");
            return ApiKit.Done(log);
        });
    }
}
