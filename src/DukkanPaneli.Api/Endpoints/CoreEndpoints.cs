using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Endpoints;

public sealed record SettingsReq(string? ShopName, decimal? DefaultVatRate);
public sealed record EventReq(string? Title, DateOnly? Date, string? Time, string? Type, string? Note, int Repeat = 0);

public static partial class CoreEndpoints
{
    private static readonly HashSet<string> EventTypes = ["odeme", "teslimat", "toplanti", "kampanya", "diger"];

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimeRx();

    public static async Task<Dictionary<string, string>> SettingsAsync(AppDb db) =>
        await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);

    private static async Task SetSetting(AppDb db, string key, string value)
    {
        var s = await db.Settings.FindAsync(key);
        if (s is null) db.Settings.Add(new Setting { Key = key, Value = value });
        else s.Value = value;
    }

    public static void MapCoreEndpoints(this IEndpointRouteBuilder app)
    {
        var all = app.MapGroup("/api").RequireAuthorization();
        var owner = app.MapGroup("/api").RequireAuthorization("Sahip");

        // Ekranların ihtiyaç duyduğu temel veriler. Çalışan, finansal verileri ve maliyetleri görmez.
        all.MapGet("/bootstrap", async (HttpContext ctx, AppDb db) =>
        {
            var actor = ApiKit.ActorOf(ctx.User);
            var me = await db.Users.AsNoTracking().Where(u => u.Id == actor.Id).Select(u => new { u.Id, u.UserName, u.DisplayName, u.Role }).FirstAsync();
            var st = await SettingsAsync(db);
            var owner = actor.IsOwner;
            var products = await db.Products.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync();
            var suppliers = await db.Suppliers.AsNoTracking().Where(s => !s.IsArchived).OrderBy(s => s.Name).ToListAsync();
            var events = await db.Events.AsNoTracking().OrderBy(e => e.Date).ToListAsync();
            object? cash = null, debts = null;
            if (owner)
            {
                cash = await db.CashEntries.AsNoTracking().OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
                    .Select(c => new { c.Id, c.Type, c.Amount, c.VatAmount, c.Category, c.Date, c.Note, c.CreatedBy, c.SaleId, linked = c.SaleId != null || c.DebtPaymentId != null || c.StockMovementId != null })
                    .ToListAsync();
                debts = (await db.Debts.AsNoTracking().Include(d => d.Payments).ToListAsync()).Select(FinanceEndpoints.Dto).ToList();
            }
            return Results.Ok(new
            {
                me,
                settings = new
                {
                    shopName = st.GetValueOrDefault("shopName", "Dükkanım"),
                    defaultVatRate = decimal.TryParse(st.GetValueOrDefault("defaultVatRate"), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 20,
                },
                products = products.Select(p => new { p.Id, p.Name, p.Category, p.Unit, p.Stock, p.Capacity, p.MinStock, cost = owner ? p.Cost : (decimal?)null, p.Price, p.VatRate, p.SupplierId }),
                suppliers = suppliers.Select(s => new { s.Id, s.Name, s.Phone, s.Email, s.Note }),
                events,
                cash,
                debts,
            });
        });

        owner.MapPut("/settings", async (SettingsReq r, HttpContext ctx, AppDb db) =>
        {
            var name = ApiKit.Clean(r.ShopName, 60);
            if (name.Length == 0) return ApiKit.Fail("Dükkan adı boş olamaz.");
            if (r.DefaultVatRate is < 0 or > 100) return ApiKit.Fail("KDV oranı 0-100 arasında olmalı.");
            await SetSetting(db, "shopName", name);
            if (r.DefaultVatRate is { } vat) await SetSetting(db, "defaultVatRate", vat.ToString(CultureInfo.InvariantCulture));
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "settings.update", () => $"Ayarlar güncellendi: {name}");
            return ApiKit.Done(log);
        });

        /* ---------------- Takvim ---------------- */
        string? ValidateEvent(EventReq r)
        {
            if (string.IsNullOrWhiteSpace(r.Title)) return "Başlık gerekli.";
            if (r.Date is null) return "Tarih seçin.";
            if (!string.IsNullOrEmpty(r.Time) && !TimeRx().IsMatch(r.Time)) return "Saat SS:dd biçiminde olmalı.";
            if (r.Repeat is < 0 or > 24) return "Tekrar 0-24 ay arasında olmalı.";
            return null;
        }

        all.MapPost("/events", async (EventReq r, HttpContext ctx, AppDb db) =>
        {
            if (ValidateEvent(r) is { } err) return ApiKit.Fail(err);
            var type = EventTypes.Contains(r.Type ?? "") ? r.Type! : "diger";
            var times = Math.Max(1, r.Repeat);
            var d0 = r.Date!.Value;
            for (var i = 0; i < times; i++)
            {
                // AddMonths ay sonunu korur: 31 Ocak → 28/29 Şubat.
                db.Events.Add(new CalendarEvent { Title = ApiKit.Clean(r.Title, 80), Date = d0.AddMonths(i), Time = r.Time ?? "", Type = type, Note = ApiKit.Clean(r.Note, 200) });
            }
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "event.create",
                () => $"Etkinlik eklendi: {ApiKit.Clean(r.Title, 80)} ({d0:dd.MM.yyyy}{(times > 1 ? $", {times} ay tekrar" : "")})");
            return ApiKit.Done(log);
        });

        all.MapPut("/events/{id:int}", async (int id, EventReq r, HttpContext ctx, AppDb db) =>
        {
            var e = await db.Events.FindAsync(id);
            if (e is null) return ApiKit.Fail("Etkinlik bulunamadı.", 404);
            if (ValidateEvent(r) is { } err) return ApiKit.Fail(err);
            e.Title = ApiKit.Clean(r.Title, 80); e.Date = r.Date!.Value; e.Time = r.Time ?? ""; e.Type = EventTypes.Contains(r.Type ?? "") ? r.Type! : "diger"; e.Note = ApiKit.Clean(r.Note, 200);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "event.update", () => $"Etkinlik güncellendi: {e.Title} ({e.Date:dd.MM.yyyy})");
            return ApiKit.Done(log);
        });

        all.MapDelete("/events/{id:int}", async (int id, HttpContext ctx, AppDb db) =>
        {
            var e = await db.Events.FindAsync(id);
            if (e is null) return ApiKit.Fail("Etkinlik bulunamadı.", 404);
            db.Events.Remove(e);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "event.delete", () => $"Etkinlik silindi: {e.Title} ({e.Date:dd.MM.yyyy})");
            return ApiKit.Done(log);
        });

        /* ---------------- Bildirimler ---------------- */
        all.MapGet("/notifications", async (HttpContext ctx, AppDb db) =>
        {
            var actor = ApiKit.ActorOf(ctx.User);
            var today = ApiKit.Today;
            var list = new List<object>();
            foreach (var p in (await db.Products.AsNoTracking().Where(p => !p.IsArchived).ToListAsync()).Where(p => p.Stock <= p.MinStock))
                list.Add(new { key = $"low-{p.Id}-{p.Stock}", level = "warn", title = "Kritik stok", body = $"{p.Name}: {ApiKit.Qty(p.Stock)} {p.Unit} kaldı (min. {ApiKit.Qty(p.MinStock)})", link = "#urunler" });
            foreach (var e in await db.Events.AsNoTracking().Where(e => e.Date >= today && e.Date <= today.AddDays(1)).OrderBy(e => e.Date).ToListAsync())
                list.Add(new { key = $"ev-{e.Id}-{e.Date}", level = "info", title = e.Date == today ? "Bugün" : "Yarın", body = $"{e.Title}{(e.Time.Length > 0 ? " · " + e.Time : "")}", link = "#takvim" });
            if (actor.IsOwner)
            {
                foreach (var d in (await db.Debts.AsNoTracking().Include(d => d.Payments).Where(d => d.DueDate != null && d.DueDate <= today.AddDays(3)).ToListAsync()).Where(d => FinanceEndpoints.RemainingOf(d) > 0))
                {
                    var days = d.DueDate!.Value.DayNumber - today.DayNumber;
                    var what = d.Kind == DebtKind.Borc ? "Ödeme" : "Tahsilat";
                    list.Add(new
                    {
                        key = $"due-{d.Id}-{d.DueDate}-{(days < 0 ? "late" : days.ToString())}",
                        level = days < 0 ? "danger" : "warn",
                        title = days < 0 ? $"{what} gecikti" : days == 0 ? $"{what} bugün" : $"{what} {days} gün sonra",
                        body = $"{d.Party}: {ApiKit.Money(FinanceEndpoints.RemainingOf(d))}",
                        link = "#borclar",
                    });
                }
            }
            return Results.Ok(list);
        });

        /* ---------------- Hareket geçmişi ve geri alma ---------------- */
        owner.MapGet("/history", async (DateOnly? from, DateOnly? to, string? user, string? q, int? page, AppDb db) =>
        {
            var query = db.AuditLogs.AsNoTracking().AsQueryable();
            if (from is { } f) { var fu = f.ToDateTime(TimeOnly.MinValue).ToUniversalTime(); query = query.Where(a => a.At >= fu); }
            if (to is { } t) { var tu = t.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime(); query = query.Where(a => a.At < tu); }
            if (!string.IsNullOrWhiteSpace(user)) query = query.Where(a => a.UserName == user);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(a => a.Summary.ToLower().Contains(q.ToLower()));
            var total = await query.CountAsync();
            var size = 50;
            var pg = Math.Max(1, page ?? 1);
            var items = await query.OrderByDescending(a => a.Id).Skip((pg - 1) * size).Take(size).ToListAsync();
            var users = await db.AuditLogs.AsNoTracking().Select(a => a.UserName).Distinct().ToListAsync();
            return Results.Ok(new
            {
                total, page = pg, pageSize = size, users,
                items = items.Select(a => new { a.Id, a.At, a.UserName, a.Action, a.Summary, a.Undoable, a.UndoneAt, a.UndoneByAuditId, a.UndoOfAuditId, changeCount = AppDb.ParseChanges(a.Changes).Count }),
            });
        });

        // Ayrıntı: hangi kayıtta hangi alan neyden neye değişti.
        owner.MapGet("/history/{id:int}", async (int id, AppDb db) =>
        {
            var a = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return ApiKit.Fail("Kayıt bulunamadı.", 404);
            var changes = AppDb.ParseChanges(a.Changes).Select(c =>
            {
                var keys = (c.After ?? c.Before)!.Keys;
                var fields = keys
                    .Where(k => c.State != "Modified" || c.Before![k].GetRawText() != c.After![k].GetRawText())
                    .Where(k => c.State == "Modified" || k is not ("CreatedAt"))
                    .Select(k => new { field = k, before = c.Before is null ? null : Show(c.Before[k]), after = c.After is null ? null : Show(c.After[k]) });
                var id0 = (c.After ?? c.Before)!.TryGetValue("Id", out var idEl) ? Show(idEl) : (c.After ?? c.Before)!.TryGetValue("Key", out var k2) ? Show(k2) : "";
                return new { entity = c.Entity.Split('.').Last(), label = AppDb.EntityLabel(c.Entity), key = id0, state = c.State, fields };
            });
            return Results.Ok(new { a.Id, a.At, a.UserName, a.Action, a.Summary, a.Undoable, a.UndoneAt, a.UndoneByAuditId, a.UndoOfAuditId, changes });
        });

        owner.MapPost("/history/{id:int}/undo", async (int id, HttpContext ctx, AppDb db) =>
        {
            var res = await db.UndoAsync(id, ApiKit.ActorOf(ctx.User));
            return res.Error is not null ? ApiKit.Fail(res.Error, 409, res.Blockers) : ApiKit.Done(res.Log!);
        });

        /* ---------------- Yönetim ---------------- */
        owner.MapPost("/admin/demo", async (HttpContext ctx, AppDb db) =>
        {
            var log = await DemoData.LoadAsync(db, ApiKit.ActorOf(ctx.User));
            return ApiKit.Done(log);
        });

        owner.MapPost("/admin/clear", async (HttpContext ctx, AppDb db) =>
        {
            await DemoData.ClearAsync(db);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "data.clear", () => "Tüm işletme verileri silindi", undoable: false);
            return ApiKit.Done(log);
        });

        // Veritabanının tutarlı bir kopyası (SQLite VACUUM INTO).
        owner.MapGet("/admin/backup", async (AppDb db) =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"dukkan-yedek-{Guid.NewGuid():N}.db");
            try
            {
                await db.Database.ExecuteSqlAsync($"VACUUM INTO {tmp}");
                var bytes = await File.ReadAllBytesAsync(tmp);
                return Results.File(bytes, "application/vnd.sqlite3", $"dukkan-yedek-{DateTime.Now:yyyy-MM-dd-HHmm}.db");
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        });
    }

    private static string? Show(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => "evet",
        JsonValueKind.False => "hayır",
        _ => e.GetRawText(),
    };
}
