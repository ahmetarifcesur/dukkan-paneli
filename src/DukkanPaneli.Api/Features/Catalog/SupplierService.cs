using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Catalog;

public sealed class SupplierService(AppDb db)
{
    public async Task<OpResult> CreateAsync(Actor actor, SupplierRequest r)
    {
        var name = Input.Clean(r.Name, 80);
        if (name.Length == 0) return OpResult.Fail("Firma adı gerekli.");
        if (await NameTakenAsync(name, exceptId: null)) return OpResult.Fail("Bu isimde bir tedarikçi zaten var.");

        var s = new Supplier { Name = name, Phone = Input.Clean(r.Phone, 25), Email = Input.Clean(r.Email, 80), Note = Input.Clean(r.Note, 200) };
        db.Suppliers.Add(s);
        var log = await db.SaveAuditedAsync(actor, "supplier.create", () => $"Tedarikçi eklendi: {s.Name}");
        return OpResult.Ok(log, new { s.Id });
    }

    public async Task<OpResult> UpdateAsync(Actor actor, int id, SupplierRequest r)
    {
        var s = await db.Suppliers.FindAsync(id);
        if (s is null || s.IsArchived) return OpResult.NotFound("Tedarikçi bulunamadı.");
        var name = Input.Clean(r.Name, 80);
        if (name.Length == 0) return OpResult.Fail("Firma adı gerekli.");
        if (await NameTakenAsync(name, exceptId: id)) return OpResult.Fail("Bu isimde bir tedarikçi zaten var.");

        // Borç kayıtlarında firma adı kopyalanarak tutulur; ad değişirse onları da güncelle.
        if (name != s.Name)
            foreach (var d in await db.Debts.Where(d => d.SupplierId == id).ToListAsync())
                d.Party = name;

        s.Name = name;
        s.Phone = Input.Clean(r.Phone, 25);
        s.Email = Input.Clean(r.Email, 80);
        s.Note = Input.Clean(r.Note, 200);
        var log = await db.SaveAuditedAsync(actor, "supplier.update", () => $"Tedarikçi güncellendi: {s.Name}");
        return OpResult.Ok(log);
    }

    /// <summary>Arşivler ve ürünlerden tedarikçi bağlantısını kaldırır (borç kayıtları kalır).</summary>
    public async Task<OpResult> ArchiveAsync(Actor actor, int id)
    {
        var s = await db.Suppliers.FindAsync(id);
        if (s is null || s.IsArchived) return OpResult.NotFound("Tedarikçi bulunamadı.");
        s.IsArchived = true;
        foreach (var p in await db.Products.Where(p => p.SupplierId == id).ToListAsync())
            p.SupplierId = null;
        var log = await db.SaveAuditedAsync(actor, "supplier.archive", () => $"Tedarikçi silindi: {s.Name}");
        return OpResult.Ok(log);
    }

    private async Task<bool> NameTakenAsync(string name, int? exceptId) =>
        (await db.Suppliers.Where(s => s.Id != exceptId && !s.IsArchived).Select(s => s.Name).ToListAsync())
            .Any(n => Input.SameText(n, name));
}
