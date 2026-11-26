using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Endpoints;

public sealed record CashReq(CashType Type, decimal? Amount, decimal? VatRate, string? Category, DateOnly? Date, string? Note);
public sealed record DebtReq(DebtKind Kind, string? Party, decimal? Amount, decimal? OpeningPaid, DateOnly? Date, DateOnly? DueDate, string? Note);
public sealed record PaymentReq(decimal? Amount, DateOnly? Date, bool Record = true);

public static class FinanceEndpoints
{
    /// <summary>Otomatik oluşmuş (bağlı) kayıtlar elle değiştirilemez; aksi hâlde stok/satış/borç ile tutarsızlık oluşur.</summary>
    public static string? LinkedReason(CashEntry c) =>
        c.SaleId is int s ? $"Bu kayıt {ApiKit.SaleNo(s)} numaralı satıştan otomatik oluştu. Değiştirmek için satışı iptal edin veya Geçmiş ekranından geri alın."
        : c.DebtPaymentId is not null ? "Bu kayıt bir borç ödemesi/tahsilatından oluştu. Borç & Alacak ekranından ödemeyi iptal edin."
        : c.StockMovementId is not null ? "Bu kayıt bir stok girişinden oluştu. Geçmiş ekranından stok girişini geri alın."
        : null;

    public static decimal PaidOf(Debt d) => d.OpeningPaid + d.Payments.Sum(p => p.Amount);
    public static decimal RemainingOf(Debt d) => d.Amount - PaidOf(d);

    public static object Dto(Debt d) => new
    {
        d.Id, d.Kind, d.Party, d.SupplierId, d.Amount, d.OpeningPaid, paid = PaidOf(d), remaining = RemainingOf(d), d.Date, d.DueDate, d.Note,
        d.SaleId, saleNo = d.SaleId is int s ? ApiKit.SaleNo(s) : null, linked = d.SaleId is not null || d.StockMovementId is not null,
        payments = d.Payments.OrderBy(p => p.Date).ThenBy(p => p.Id).Select(p => new { p.Id, p.Amount, p.Date, p.CreatedBy }),
    };

    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var owner = app.MapGroup("/api").RequireAuthorization("Sahip");

        /* ---------------- Gelir / gider ---------------- */
        owner.MapPost("/cash", async (CashReq r, HttpContext ctx, AppDb db) =>
        {
            if (r.Amount is not > 0) return ApiKit.Fail("Tutar sıfırdan büyük olmalı.");
            if (r.VatRate is < 0 or > 100) return ApiKit.Fail("KDV oranı 0-100 arasında olmalı.");
            var actor = ApiKit.ActorOf(ctx.User);
            var amount = ApiKit.R2(r.Amount.Value);
            var c = new CashEntry { Type = r.Type, Amount = amount, VatAmount = ApiKit.VatOf(amount, r.VatRate ?? 0), Category = ApiKit.Clean(r.Category, 40) is { Length: > 0 } cat ? cat : "Diğer", Date = r.Date ?? ApiKit.Today, Note = ApiKit.Clean(r.Note, 120), CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name };
            db.CashEntries.Add(c);
            var log = await db.SaveAuditedAsync(actor, "cash.create", () => $"{(c.Type == CashType.Gelir ? "Gelir" : "Gider")} eklendi: {c.Category} {ApiKit.Money(c.Amount)}{(c.Note.Length > 0 ? " — " + c.Note : "")}");
            return ApiKit.Done(log, new { c.Id });
        });

        owner.MapPut("/cash/{id:int}", async (int id, CashReq r, HttpContext ctx, AppDb db) =>
        {
            var c = await db.CashEntries.FindAsync(id);
            if (c is null) return ApiKit.Fail("Kayıt bulunamadı.", 404);
            if (LinkedReason(c) is { } why) return ApiKit.Fail(why);
            if (r.Amount is not > 0) return ApiKit.Fail("Tutar sıfırdan büyük olmalı.");
            if (r.VatRate is < 0 or > 100) return ApiKit.Fail("KDV oranı 0-100 arasında olmalı.");
            var old = c.Amount;
            c.Type = r.Type; c.Amount = ApiKit.R2(r.Amount.Value); c.VatAmount = ApiKit.VatOf(c.Amount, r.VatRate ?? 0);
            c.Category = ApiKit.Clean(r.Category, 40) is { Length: > 0 } cat ? cat : "Diğer"; c.Date = r.Date ?? c.Date; c.Note = ApiKit.Clean(r.Note, 120);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "cash.update",
                () => $"Kayıt düzenlendi: {c.Category}" + (old != c.Amount ? $" ({ApiKit.Money(old)} → {ApiKit.Money(c.Amount)})" : ""));
            return ApiKit.Done(log);
        });

        owner.MapDelete("/cash/{id:int}", async (int id, HttpContext ctx, AppDb db) =>
        {
            var c = await db.CashEntries.FindAsync(id);
            if (c is null) return ApiKit.Fail("Kayıt bulunamadı.", 404);
            if (LinkedReason(c) is { } why) return ApiKit.Fail(why);
            db.CashEntries.Remove(c);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "cash.delete", () => $"Kayıt silindi: {c.Category} {ApiKit.Money(c.Amount)} ({c.Date:dd.MM.yyyy})");
            return ApiKit.Done(log);
        });

        /* ---------------- Borç / alacak ---------------- */
        owner.MapPost("/debts", async (DebtReq r, HttpContext ctx, AppDb db) =>
        {
            var party = ApiKit.Clean(r.Party, 80);
            if (party.Length == 0) return ApiKit.Fail("Kişi / firma adı gerekli.");
            if (r.Amount is not > 0) return ApiKit.Fail("Tutar sıfırdan büyük olmalı.");
            var opening = r.OpeningPaid ?? 0;
            if (opening < 0 || opening > r.Amount) return ApiKit.Fail("Ödenmiş kısım 0 ile toplam tutar arasında olmalı.");
            var date = r.Date ?? ApiKit.Today;
            if (r.DueDate is { } due && due < date) return ApiKit.Fail("Vade tarihi kayıt tarihinden önce olamaz.");
            var sup = r.Kind == DebtKind.Borc ? await db.Suppliers.FirstOrDefaultAsync(s => !s.IsArchived && s.Name.ToLower() == party.ToLower()) : null;
            var d = new Debt { Kind = r.Kind, Party = sup?.Name ?? party, SupplierId = sup?.Id, Amount = ApiKit.R2(r.Amount.Value), OpeningPaid = ApiKit.R2(opening), Date = date, DueDate = r.DueDate, Note = ApiKit.Clean(r.Note, 120), CreatedAt = DateTime.UtcNow };
            db.Debts.Add(d);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "debt.create", () => $"{(d.Kind == DebtKind.Borc ? "Borç" : "Alacak")} eklendi: {d.Party} {ApiKit.Money(d.Amount)}");
            return ApiKit.Done(log, new { d.Id });
        });

        owner.MapPut("/debts/{id:int}", async (int id, DebtReq r, HttpContext ctx, AppDb db) =>
        {
            var d = await db.Debts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return ApiKit.Fail("Kayıt bulunamadı.", 404);
            var party = ApiKit.Clean(r.Party, 80);
            if (party.Length == 0) return ApiKit.Fail("Kişi / firma adı gerekli.");
            if (r.Amount is not > 0) return ApiKit.Fail("Tutar sıfırdan büyük olmalı.");
            var linked = d.SaleId is not null || d.StockMovementId is not null;
            if (linked && (r.Amount != d.Amount || r.Kind != d.Kind)) return ApiKit.Fail("Bu kayıt bir satış/stok girişine bağlı; tutarı ve türü değiştirilemez.");
            var opening = r.OpeningPaid ?? d.OpeningPaid;
            if (opening < 0) return ApiKit.Fail("Ödenmiş kısım negatif olamaz.");
            if (opening + d.Payments.Sum(p => p.Amount) > r.Amount) return ApiKit.Fail("Ödenen toplam, tutardan büyük olamaz.");
            var date = r.Date ?? d.Date;
            if (r.DueDate is { } due && due < date) return ApiKit.Fail("Vade tarihi kayıt tarihinden önce olamaz.");
            d.Kind = r.Kind; d.Party = party; d.Amount = ApiKit.R2(r.Amount.Value); d.OpeningPaid = ApiKit.R2(opening); d.Date = date; d.DueDate = r.DueDate; d.Note = ApiKit.Clean(r.Note, 120);
            if (d.SupplierId is null && d.Kind == DebtKind.Borc)
                d.SupplierId = (await db.Suppliers.FirstOrDefaultAsync(s => !s.IsArchived && s.Name.ToLower() == party.ToLower()))?.Id;
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "debt.update", () => $"Borç/alacak düzenlendi: {d.Party}");
            return ApiKit.Done(log);
        });

        owner.MapDelete("/debts/{id:int}", async (int id, HttpContext ctx, AppDb db) =>
        {
            var d = await db.Debts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return ApiKit.Fail("Kayıt bulunamadı.", 404);
            if (d.SaleId is int s) return ApiKit.Fail($"Bu alacak {ApiKit.SaleNo(s)} numaralı veresiye satıştan oluştu. Satışı iptal edin.");
            if (d.StockMovementId is not null) return ApiKit.Fail("Bu borç vadeli bir stok girişinden oluştu. Geçmiş ekranından stok girişini geri alın.");
            if (d.Payments.Count > 0) return ApiKit.Fail("Bu kaydın ödemeleri var. Önce ödemeleri iptal edin.");
            db.Debts.Remove(d);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "debt.delete", () => $"Borç/alacak silindi: {d.Party} {ApiKit.Money(d.Amount)}");
            return ApiKit.Done(log);
        });

        owner.MapPost("/debts/{id:int}/payments", async (int id, PaymentReq r, HttpContext ctx, AppDb db) =>
        {
            var d = await db.Debts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id);
            if (d is null) return ApiKit.Fail("Kayıt bulunamadı.", 404);
            var remaining = RemainingOf(d);
            if (r.Amount is not > 0) return ApiKit.Fail("Tutar sıfırdan büyük olmalı.");
            var amount = ApiKit.R2(r.Amount.Value);
            if (amount > remaining) return ApiKit.Fail($"Tutar kalan miktardan ({ApiKit.Money(remaining)}) büyük olamaz.");
            var actor = ApiKit.ActorOf(ctx.User);
            var pay = new DebtPayment { Amount = amount, Date = r.Date ?? ApiKit.Today, CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name };
            d.Payments.Add(pay);
            var isBorc = d.Kind == DebtKind.Borc;
            if (r.Record)
                db.CashEntries.Add(new CashEntry
                {
                    Type = isBorc ? CashType.Gider : CashType.Gelir, Amount = amount,
                    Category = isBorc ? (d.SupplierId is not null || d.StockMovementId is not null ? "Tedarik" : "Borç ödemesi") : "Tahsilat",
                    Date = pay.Date, Note = $"{d.Party} — {(isBorc ? "borç ödemesi" : "tahsilat")}", CreatedAt = DateTime.UtcNow, CreatedBy = actor.Name, DebtPayment = pay,
                });
            var log = await db.SaveAuditedAsync(actor, isBorc ? "debt.pay" : "debt.collect",
                () => $"{(isBorc ? "Ödeme yapıldı" : "Tahsilat alındı")}: {d.Party} {ApiKit.Money(amount)} (kalan {ApiKit.Money(RemainingOf(d))})");
            return ApiKit.Done(log);
        });

        owner.MapDelete("/debts/{id:int}/payments/{pid:int}", async (int id, int pid, HttpContext ctx, AppDb db) =>
        {
            var d = await db.Debts.Include(x => x.Payments).FirstOrDefaultAsync(x => x.Id == id);
            var pay = d?.Payments.FirstOrDefault(p => p.Id == pid);
            if (d is null || pay is null) return ApiKit.Fail("Ödeme bulunamadı.", 404);
            var cash = await db.CashEntries.FirstOrDefaultAsync(c => c.DebtPaymentId == pid);
            if (cash is not null) db.CashEntries.Remove(cash);
            db.DebtPayments.Remove(pay);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "debt.payment.delete", () => $"Ödeme iptal edildi: {d.Party} {ApiKit.Money(pay.Amount)} ({pay.Date:dd.MM.yyyy})");
            return ApiKit.Done(log);
        });
    }
}
