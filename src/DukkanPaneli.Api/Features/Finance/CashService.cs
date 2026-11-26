using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Finance;

/// <summary>Elle girilen gelir/gider kayıtları (kira, fatura, maaş...).</summary>
public sealed class CashService(AppDb db)
{
    /// <summary>
    /// Otomatik oluşmuş (bağlı) kayıt elle değiştirilirse satış/stok/borç ile tutarsızlık oluşur.
    /// Bu yüzden kilitlidir; mesaj kullanıcıya doğru yolu gösterir. Kilitli değilse null.
    /// </summary>
    public static string? LockReason(CashEntry c) =>
        c.SaleId is int s ? $"Bu kayıt {Format.SaleNo(s)} numaralı satıştan otomatik oluştu. Değiştirmek için satışı iptal edin veya Geçmiş ekranından geri alın."
        : c.DebtPaymentId is not null ? "Bu kayıt bir borç ödemesi/tahsilatından oluştu. Borç & Alacak ekranından ödemeyi iptal edin."
        : c.StockMovementId is not null ? "Bu kayıt bir stok girişinden oluştu. Geçmiş ekranından stok girişini geri alın."
        : null;

    public Task<List<CashDto>> ListAsync() =>
        db.CashEntries.AsNoTracking()
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(c => new CashDto(c.Id, c.Type, c.Amount, c.VatAmount, c.Category, c.Date, c.Note, c.CreatedBy, c.SaleId,
                c.SaleId != null || c.DebtPaymentId != null || c.StockMovementId != null))
            .ToListAsync();

    public async Task<OpResult> CreateAsync(Actor actor, CashRequest r)
    {
        if (Validate(r) is { } error) return OpResult.Fail(error);
        var amount = MoneyMath.Round(r.Amount!.Value);
        var c = new CashEntry
        {
            Type = r.Type, Amount = amount, VatAmount = MoneyMath.VatOf(amount, r.VatRate ?? 0),
            Category = Input.CleanOr(r.Category, 40, "Diğer"), Date = r.Date ?? Clock.Today, Note = Input.Clean(r.Note, 120),
            CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name,
        };
        db.CashEntries.Add(c);
        var log = await db.SaveAuditedAsync(actor, "cash.create",
            () => $"{Format.Label(c.Type)} eklendi: {c.Category} {Format.Money(c.Amount)}{(c.Note.Length > 0 ? " — " + c.Note : "")}");
        return OpResult.Ok(log, new { c.Id });
    }

    public async Task<OpResult> UpdateAsync(Actor actor, int id, CashRequest r)
    {
        var c = await db.CashEntries.FindAsync(id);
        if (c is null) return OpResult.NotFound("Kayıt bulunamadı.");
        if (LockReason(c) is { } locked) return OpResult.Fail(locked);
        if (Validate(r) is { } error) return OpResult.Fail(error);

        var oldAmount = c.Amount;
        c.Type = r.Type;
        c.Amount = MoneyMath.Round(r.Amount!.Value);
        c.VatAmount = MoneyMath.VatOf(c.Amount, r.VatRate ?? 0);
        c.Category = Input.CleanOr(r.Category, 40, "Diğer");
        c.Date = r.Date ?? c.Date;
        c.Note = Input.Clean(r.Note, 120);
        var log = await db.SaveAuditedAsync(actor, "cash.update",
            () => $"Kayıt düzenlendi: {c.Category}" + (oldAmount != c.Amount ? $" ({Format.Money(oldAmount)} → {Format.Money(c.Amount)})" : ""));
        return OpResult.Ok(log);
    }

    public async Task<OpResult> DeleteAsync(Actor actor, int id)
    {
        var c = await db.CashEntries.FindAsync(id);
        if (c is null) return OpResult.NotFound("Kayıt bulunamadı.");
        if (LockReason(c) is { } locked) return OpResult.Fail(locked);
        db.CashEntries.Remove(c);
        var log = await db.SaveAuditedAsync(actor, "cash.delete",
            () => $"Kayıt silindi: {c.Category} {Format.Money(c.Amount)} ({c.Date:dd.MM.yyyy})");
        return OpResult.Ok(log);
    }

    private static string? Validate(CashRequest r)
    {
        if (r.Amount is not > 0) return "Tutar sıfırdan büyük olmalı.";
        if (r.VatRate is < 0 or > 100) return "KDV oranı 0-100 arasında olmalı.";
        return null;
    }
}
