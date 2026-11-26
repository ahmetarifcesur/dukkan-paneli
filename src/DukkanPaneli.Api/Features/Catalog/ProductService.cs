using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Catalog;

/// <summary>Ürün ekleme, düzenleme, arşivleme. Stok miktarı burada değiştirilmez; bkz. <see cref="StockService"/>.</summary>
public sealed class ProductService(AppDb db)
{
    public async Task<OpResult> CreateAsync(Actor actor, ProductRequest r)
    {
        if (Validate(r, creating: true) is { } error) return OpResult.Fail(error);
        var name = Input.Clean(r.Name, 80);
        if (await NameTakenAsync(name, exceptId: null)) return OpResult.Fail("Bu isimde bir ürün zaten var.");
        if (r.SupplierId is int sid && !await SupplierExistsAsync(sid)) return OpResult.Fail("Tedarikçi bulunamadı.");

        var p = new Product
        {
            Name = name,
            Category = Input.Clean(r.Category, 40),
            Unit = Input.CleanOr(r.Unit, 15, "adet"),
            Stock = r.Stock!.Value,
            Capacity = r.Capacity!.Value,
            MinStock = r.MinStock!.Value,
            Cost = MoneyMath.Round(r.Cost!.Value),
            Price = MoneyMath.Round(r.Price!.Value),
            VatRate = r.VatRate!.Value,
            SupplierId = r.SupplierId,
        };
        db.Products.Add(p);

        // Başlangıç stoğu da bir harekettir; böylece "stok = hareketlerin toplamı" en baştan doğru olur.
        if (p.Stock > 0)
            db.StockMovements.Add(new StockMovement
            {
                Product = p, Type = MovementType.Opening, Quantity = p.Stock, StockAfter = p.Stock, UnitCost = p.Cost,
                Date = Clock.Today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, Note = "Açılış stoğu",
            });

        var log = await db.SaveAuditedAsync(actor, "product.create", () => $"Ürün eklendi: {p.Name} ({Format.Qty(p.Stock)} {p.Unit})");
        return OpResult.Ok(log, new { p.Id });
    }

    public async Task<OpResult> UpdateAsync(Actor actor, int id, ProductRequest r)
    {
        var p = await db.Products.FindAsync(id);
        if (p is null || p.IsArchived) return OpResult.NotFound("Ürün bulunamadı.");
        if (Validate(r, creating: false) is { } error) return OpResult.Fail(error);
        var name = Input.Clean(r.Name, 80);
        if (await NameTakenAsync(name, exceptId: id)) return OpResult.Fail("Bu isimde bir ürün zaten var.");
        if (r.Capacity < p.Stock) return OpResult.Fail($"Kapasite mevcut stoktan ({Format.Qty(p.Stock)}) küçük olamaz.");
        if (r.SupplierId is int sid && !await SupplierExistsAsync(sid)) return OpResult.Fail("Tedarikçi bulunamadı.");

        var oldPrice = p.Price;
        p.Name = name;
        p.Category = Input.Clean(r.Category, 40);
        p.Unit = Input.CleanOr(r.Unit, 15, p.Unit);
        p.Capacity = r.Capacity!.Value;
        p.MinStock = r.MinStock!.Value;
        p.Cost = MoneyMath.Round(r.Cost!.Value);
        p.Price = MoneyMath.Round(r.Price!.Value);
        p.VatRate = r.VatRate!.Value;
        p.SupplierId = r.SupplierId;
        // Not: r.Stock bilerek yok sayılır. Stok sadece hareketle değişir.

        var log = await db.SaveAuditedAsync(actor, "product.update",
            () => $"Ürün güncellendi: {p.Name}" + (oldPrice != p.Price ? $" (fiyat {Format.Money(oldPrice)} → {Format.Money(p.Price)})" : ""));
        return OpResult.Ok(log);
    }

    /// <summary>Silme yerine arşivleme: geçmiş satışlar ve hareketler ürüne bağlı kalır.</summary>
    public async Task<OpResult> ArchiveAsync(Actor actor, int id)
    {
        var p = await db.Products.FindAsync(id);
        if (p is null || p.IsArchived) return OpResult.NotFound("Ürün bulunamadı.");
        p.IsArchived = true;
        var log = await db.SaveAuditedAsync(actor, "product.archive", () => $"Ürün silindi (arşivlendi): {p.Name}");
        return OpResult.Ok(log);
    }

    /// <summary>Kuralları tek yerde topluyoruz; ilk bulunan hatayı döndürür, sorun yoksa null.</summary>
    private static string? Validate(ProductRequest r, bool creating)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Ürün adı gerekli.";
        if (creating && r.Stock is not >= 0) return "Stok 0 veya daha büyük olmalı.";
        if (r.Capacity is not > 0) return "Kapasite sıfırdan büyük olmalı.";
        if (r.MinStock is not >= 0) return "Kritik stok 0 veya daha büyük olmalı.";
        if (r.MinStock > r.Capacity) return "Kritik stok seviyesi kapasiteden büyük olamaz.";
        if (r.Cost is not >= 0) return "Alış fiyatı 0 veya daha büyük olmalı.";
        if (r.Price is not >= 0) return "Satış fiyatı 0 veya daha büyük olmalı.";
        if (r.VatRate is not (>= 0 and <= 100)) return "KDV oranı 0-100 arasında olmalı.";
        if (creating && r.Stock > r.Capacity) return $"Stok ({Format.Qty(r.Stock!.Value)}) kapasiteden ({Format.Qty(r.Capacity!.Value)}) büyük olamaz.";
        return null;
    }

    private async Task<bool> NameTakenAsync(string name, int? exceptId) =>
        (await db.Products.Where(p => p.Id != exceptId && !p.IsArchived).Select(p => p.Name).ToListAsync())
            .Any(n => Input.SameText(n, name));

    private Task<bool> SupplierExistsAsync(int id) => db.Suppliers.AnyAsync(s => s.Id == id && !s.IsArchived);
}
