using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Finance;

/// <summary>Borç/alacak kayıtları ve kısmi ödeme/tahsilat.</summary>
public sealed class DebtService(AppDb db)
{
    public async Task<List<DebtDto>> ListAsync() =>
        (await db.Debts.AsNoTracking().Include(d => d.Payments).ToListAsync()).Select(DebtDto.From).ToList();

    public async Task<OpResult> CreateAsync(Actor actor, DebtRequest r)
    {
        var party = Input.Clean(r.Party, 80);
        if (party.Length == 0) return OpResult.Fail("Kişi / firma adı gerekli.");
        if (r.Amount is not > 0) return OpResult.Fail("Tutar sıfırdan büyük olmalı.");
        var opening = r.OpeningPaid ?? 0;
        if (opening < 0 || opening > r.Amount) return OpResult.Fail("Ödenmiş kısım 0 ile toplam tutar arasında olmalı.");
        var date = r.Date ?? Clock.Today;
        if (r.DueDate is { } due && due < date) return OpResult.Fail("Vade tarihi kayıt tarihinden önce olamaz.");

        // Borç bir tedarikçiyeyse (aynı ad) kayda bağla: tedarikçi ekranında görünür.
        var supplier = r.Kind == DebtKind.Payable ? await FindSupplierAsync(party) : null;
        var d = new Debt
        {
            Kind = r.Kind, Party = supplier?.Name ?? party, SupplierId = supplier?.Id,
            Amount = MoneyMath.Round(r.Amount.Value), OpeningPaid = MoneyMath.Round(opening),
            Date = date, DueDate = r.DueDate, Note = Input.Clean(r.Note, 120), CreatedAt = DateTime.UtcNow,
        };
        db.Debts.Add(d);
        var log = await db.SaveAuditedAsync(actor, "debt.create", () => $"{Format.Label(d.Kind)} eklendi: {d.Party} {Format.Money(d.Amount)}");
        return OpResult.Ok(log, new { d.Id });
    }

    public async Task<OpResult> UpdateAsync(Actor actor, int id, DebtRequest r)
    {
        var d = await LoadAsync(id);
        if (d is null) return OpResult.NotFound("Kayıt bulunamadı.");
        var party = Input.Clean(r.Party, 80);
        if (party.Length == 0) return OpResult.Fail("Kişi / firma adı gerekli.");
        if (r.Amount is not > 0) return OpResult.Fail("Tutar sıfırdan büyük olmalı.");
        var amount = MoneyMath.Round(r.Amount.Value);
        if (d.IsLinked && (amount != d.Amount || r.Kind != d.Kind))
            return OpResult.Fail("Bu kayıt bir satış/stok girişine bağlı; tutarı ve türü değiştirilemez.");
        // Ödemeler kasaya türüne göre gider/gelir olarak yazıldı; tür değişirse bu kayıtlar ters düşer.
        if (r.Kind != d.Kind && d.Payments.Count > 0)
            return OpResult.Fail("Ödemesi olan kaydın türü (borç/alacak) değiştirilemez. Önce ödemeleri iptal edin.");
        var opening = r.OpeningPaid ?? d.OpeningPaid;
        if (opening < 0) return OpResult.Fail("Ödenmiş kısım negatif olamaz.");
        if (opening + d.Payments.Sum(p => p.Amount) > amount) return OpResult.Fail("Ödenen toplam, tutardan büyük olamaz.");
        var date = r.Date ?? d.Date;
        if (r.DueDate is { } due && due < date) return OpResult.Fail("Vade tarihi kayıt tarihinden önce olamaz.");

        d.Kind = r.Kind;
        d.Party = party;
        d.Amount = amount;
        d.OpeningPaid = MoneyMath.Round(opening);
        d.Date = date;
        d.DueDate = r.DueDate;
        d.Note = Input.Clean(r.Note, 120);
        if (d.SupplierId is null && d.Kind == DebtKind.Payable)
            d.SupplierId = (await FindSupplierAsync(party))?.Id;

        var log = await db.SaveAuditedAsync(actor, "debt.update", () => $"Borç/alacak düzenlendi: {d.Party}");
        return OpResult.Ok(log);
    }

    public async Task<OpResult> DeleteAsync(Actor actor, int id)
    {
        var d = await LoadAsync(id);
        if (d is null) return OpResult.NotFound("Kayıt bulunamadı.");
        if (d.SaleId is int s) return OpResult.Fail($"Bu alacak {Format.SaleNo(s)} numaralı veresiye satıştan oluştu. Satışı iptal edin.");
        if (d.StockMovementId is not null) return OpResult.Fail("Bu borç vadeli bir stok girişinden oluştu. Geçmiş ekranından stok girişini geri alın.");
        if (d.Payments.Count > 0) return OpResult.Fail("Bu kaydın ödemeleri var. Önce ödemeleri iptal edin.");
        db.Debts.Remove(d);
        var log = await db.SaveAuditedAsync(actor, "debt.delete", () => $"Borç/alacak silindi: {d.Party} {Format.Money(d.Amount)}");
        return OpResult.Ok(log);
    }

    /// <summary>Kısmi ödeme (borç) ya da tahsilat (alacak). İsteğe bağlı olarak kasaya da yazılır.</summary>
    public async Task<OpResult> AddPaymentAsync(Actor actor, int id, PaymentRequest r)
    {
        var d = await LoadAsync(id);
        if (d is null) return OpResult.NotFound("Kayıt bulunamadı.");
        if (r.Amount is not > 0) return OpResult.Fail("Tutar sıfırdan büyük olmalı.");
        var amount = MoneyMath.Round(r.Amount.Value);
        var remaining = d.Remaining;
        if (amount > remaining) return OpResult.Fail($"Tutar kalan miktardan ({Format.Money(remaining)}) büyük olamaz.");

        var payment = new DebtPayment { Amount = amount, Date = r.Date ?? Clock.Today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name };
        d.Payments.Add(payment);

        var payable = d.Kind == DebtKind.Payable;
        if (r.Record)
            db.CashEntries.Add(new CashEntry
            {
                Type = payable ? CashType.Expense : CashType.Income,
                Amount = amount,
                Category = payable ? (d.SupplierId is not null || d.StockMovementId is not null ? "Tedarik" : "Borç ödemesi") : "Tahsilat",
                Date = payment.Date,
                Note = $"{d.Party} — {(payable ? "borç ödemesi" : "tahsilat")}",
                CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name,
                DebtPayment = payment,
            });

        var log = await db.SaveAuditedAsync(actor, payable ? "debt.pay" : "debt.collect",
            () => $"{(payable ? "Ödeme yapıldı" : "Tahsilat alındı")}: {d.Party} {Format.Money(amount)} (kalan {Format.Money(d.Remaining)})");
        return OpResult.Ok(log);
    }

    /// <summary>Ödemeyi iptal eder; ona bağlı kasa kaydı da silinir.</summary>
    public async Task<OpResult> DeletePaymentAsync(Actor actor, int id, int paymentId)
    {
        var d = await LoadAsync(id);
        var payment = d?.Payments.FirstOrDefault(p => p.Id == paymentId);
        if (d is null || payment is null) return OpResult.NotFound("Ödeme bulunamadı.");
        var cash = await db.CashEntries.FirstOrDefaultAsync(c => c.DebtPaymentId == paymentId);
        if (cash is not null) db.CashEntries.Remove(cash);
        db.DebtPayments.Remove(payment);
        var log = await db.SaveAuditedAsync(actor, "debt.payment.delete",
            () => $"Ödeme iptal edildi: {d.Party} {Format.Money(payment.Amount)} ({payment.Date:dd.MM.yyyy})");
        return OpResult.Ok(log);
    }

    private Task<Debt?> LoadAsync(int id) => db.Debts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id);

    private async Task<Supplier?> FindSupplierAsync(string name) =>
        (await db.Suppliers.AsNoTracking().Where(s => !s.IsArchived).ToListAsync()).FirstOrDefault(s => Input.SameText(s.Name, name));
}
