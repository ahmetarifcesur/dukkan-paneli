using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Catalog;

/// <summary>
/// Stok değişiklikleri. Kural: <c>Product.Stock</c> her değiştiğinde aynı işlemde bir <see cref="StockMovement"/> eklenir.
/// Bu sayede geçmişteki herhangi bir tarihteki stok hesaplanabilir ve her değişikliğin nedeni bellidir.
/// </summary>
public sealed class StockService(AppDb db)
{
    /// <summary>Mal girişi. Peşinse gider, vadeliyse tedarikçiye borç kaydı oluşur.</summary>
    public async Task<OpResult> StockInAsync(Actor actor, int productId, StockInRequest r)
    {
        var p = await db.Products.FindAsync(productId);
        if (p is null || p.IsArchived) return OpResult.NotFound("Ürün bulunamadı.");
        if (r.Quantity is not > 0) return OpResult.Fail("Miktar sıfırdan büyük olmalı.");
        var free = p.Capacity - p.Stock;
        if (r.Quantity > free) return OpResult.Fail($"Kapasite aşılıyor: en fazla {Format.Qty(Math.Max(0, free))} {p.Unit} eklenebilir.");
        // Maliyet ve vadeli alım sahibin bilgisi/kararı: arayüz çalışana bu alanları göstermez, sunucu da kabul etmez.
        if (!actor.IsOwner && r.Credit) return OpResult.Fail("Vadeli alımı sadece dükkan sahibi girebilir.", StatusCodes.Status403Forbidden);
        var unitCost = actor.IsOwner ? r.UnitCost ?? p.Cost : p.Cost;
        if (unitCost < 0) return OpResult.Fail("Alış fiyatı negatif olamaz.");

        Supplier? supplier = null;
        if (r.SupplierId is int sid)
        {
            supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == sid && !s.IsArchived);
            if (supplier is null) return OpResult.Fail("Tedarikçi bulunamadı.");
        }
        if (r.Credit && supplier is null) return OpResult.Fail("Vadeli alım için tedarikçi seçin.");
        if (CheckDate(actor, r.Date) is { } dateError) return OpResult.Fail(dateError);
        var date = r.Date ?? Clock.Today;
        if (r.Credit && r.DueDate is { } due && due < date) return OpResult.Fail("Vade tarihi alım tarihinden önce olamaz.");

        var qty = r.Quantity.Value;
        var total = MoneyMath.Round(qty * unitCost);
        p.Stock += qty;
        p.Cost = MoneyMath.Round(unitCost); // son alış fiyatı
        if (supplier is not null) p.SupplierId = supplier.Id;

        var description = $"{Format.Qty(qty)} {p.Unit} {p.Name}";
        var movement = new StockMovement
        {
            ProductId = p.Id, Type = MovementType.StockIn, Quantity = qty, StockAfter = p.Stock, UnitCost = p.Cost,
            Date = date, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name,
            Note = Input.CleanOr(r.Note, 120, supplier?.Name ?? ""),
        };
        db.StockMovements.Add(movement);

        if (r.Credit)
            db.Debts.Add(new Debt
            {
                Kind = DebtKind.Payable, Party = supplier!.Name, SupplierId = supplier.Id, Amount = total,
                Date = date, DueDate = r.DueDate ?? date.AddDays(30), Note = $"Vadeli alım: {description}",
                CreatedAt = DateTime.UtcNow, StockMovement = movement,
            });
        else
            db.CashEntries.Add(new CashEntry
            {
                Type = CashType.Expense, Amount = total, VatAmount = MoneyMath.VatOf(total, p.VatRate), Category = "Tedarik",
                Date = date, Note = description + (supplier is null ? "" : $" — {supplier.Name}"),
                CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, StockMovement = movement,
            });

        var log = await db.SaveAuditedAsync(actor, "stock.in",
            () => $"Stok girişi: {description}, {Format.Money(total)}{(r.Credit ? " (vadeli)" : "")}");
        return OpResult.Ok(log);
    }

    /// <summary>Fire, tedarikçiye iade, sayım ve düzeltme.</summary>
    public async Task<OpResult> AdjustAsync(Actor actor, int productId, AdjustRequest r)
    {
        var p = await db.Products.FindAsync(productId);
        if (p is null || p.IsArchived) return OpResult.NotFound("Ürün bulunamadı.");
        if (r.Quantity is not { } q) return OpResult.Fail("Miktar gerekli.");
        if (CheckDate(actor, r.Date) is { } dateError) return OpResult.Fail(dateError);

        // Her tür için stoğa eklenecek işaretli fark (delta).
        decimal? delta = r.Type switch
        {
            MovementType.Waste or MovementType.ReturnToSupplier => -Math.Abs(q), // çıkış
            MovementType.Count => q - p.Stock,                                   // sayılan − kayıtlı
            MovementType.Correction => q,                                        // + ya da −
            _ => null,                                                           // diğer türler burada kullanılamaz
        };
        if (delta is not { } d) return OpResult.Fail("Geçersiz hareket türü.");
        if (r.Type is MovementType.Waste or MovementType.ReturnToSupplier && q <= 0) return OpResult.Fail("Miktar sıfırdan büyük olmalı.");
        if (r.Type == MovementType.Count && q < 0) return OpResult.Fail("Sayılan miktar negatif olamaz.");
        if (d == 0) return OpResult.Fail(r.Type == MovementType.Count ? "Sayım sonucu mevcut stokla aynı; değişiklik yok." : "Miktar sıfır olamaz.");

        var newStock = p.Stock + d;
        if (newStock < 0) return OpResult.Fail($"Stok eksiye düşemez (mevcut {Format.Qty(p.Stock)} {p.Unit}).");
        if (newStock > p.Capacity) return OpResult.Fail($"Stok kapasiteyi ({Format.Qty(p.Capacity)}) aşamaz. Önce ürünün kapasitesini artırın.");
        if (r.Type == MovementType.Correction && string.IsNullOrWhiteSpace(r.Note))
            return OpResult.Fail("Düzeltme için açıklama yazın (neden düzeltildiği geçmişte görünür).");

        p.Stock = newStock;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = p.Id, Type = r.Type, Quantity = d, StockAfter = newStock, UnitCost = p.Cost,
            Date = r.Date ?? Clock.Today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Note = Input.Clean(r.Note, 120),
        });

        var log = await db.SaveAuditedAsync(actor, "stock.adjust",
            () => $"{Format.Label(r.Type)}: {p.Name} {(d > 0 ? "+" : "")}{Format.Qty(d)} {p.Unit} (yeni stok {Format.Qty(newStock)})");
        return OpResult.Ok(log);
    }

    /// <summary>
    /// Tarih kuralı (satıştaki ile aynı): çalışan sadece bugüne kayıt girer; kimse ileri tarihe giremez,
    /// çünkü gelecekteki bir hareket "şu tarihteki stok" hesabını bozar.
    /// </summary>
    private static string? CheckDate(Actor actor, DateOnly? date) => date switch
    {
        null => null,
        { } d when d > Clock.Today => "İleri bir tarihe stok hareketi girilemez.",
        { } d when !actor.IsOwner && d != Clock.Today => "Çalışanlar sadece bugünün tarihine stok hareketi girebilir.",
        _ => null,
    };

    /// <summary>
    /// Ürünün hareket geçmişi. <paramref name="at"/> verilirse o günün sonundaki stok da hesaplanır:
    /// güncel stoktan, o tarihten SONRAKİ hareketler geri çıkarılır.
    /// </summary>
    public async Task<ProductMovementsDto?> ProductMovementsAsync(int productId, DateOnly? at, bool showCost)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId);
        if (p is null) return null;
        var list = await db.StockMovements.AsNoTracking().Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToListAsync();

        decimal? stockAt = at is { } day ? p.Stock - list.Where(m => m.Date > day).Sum(m => m.Quantity) : null;

        return new ProductMovementsDto(
            new ProductBrief(p.Id, p.Name, p.Unit, p.Stock), stockAt, at,
            list.Take(500).Select(m => new ProductMovementDto(m.Id, m.Date, m.CreatedAt, m.CreatedBy, m.Type, Format.Label(m.Type),
                m.Quantity, m.StockAfter, showCost ? m.UnitCost : null, m.Note, m.SaleId, MovementText.SaleNo(m.SaleId))));
    }

    /// <summary>Tüm ürünlerin hareketleri (dönem ve tür filtresiyle).</summary>
    public async Task<List<MovementDto>> MovementsAsync(DateRange range, MovementType? type)
    {
        var q = db.StockMovements.AsNoTracking().Where(m => m.Date >= range.From && m.Date <= range.To);
        if (type is { } t) q = q.Where(m => m.Type == t);
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id);
        var list = await q.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).Take(2000).ToListAsync();
        return list.Select(m =>
        {
            var p = products.GetValueOrDefault(m.ProductId);
            return new MovementDto(m.Id, m.Date, m.CreatedAt, m.CreatedBy, m.ProductId, p?.Name, p?.Unit,
                m.Type, Format.Label(m.Type), m.Quantity, m.StockAfter, m.Note, MovementText.SaleNo(m.SaleId));
        }).ToList();
    }
}
