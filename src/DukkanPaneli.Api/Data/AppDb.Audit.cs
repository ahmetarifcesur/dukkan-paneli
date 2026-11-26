using System.Text.Json;
using System.Text.Json.Serialization;
using DukkanPaneli.Api.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DukkanPaneli.Api.Data;

/// <summary>Tek bir kaydın değişikliği: durum (Added / Modified / Deleted) ve önceki/sonraki alan değerleri.</summary>
public sealed record EntityChange(string Entity, string State, Dictionary<string, JsonElement>? Before, Dictionary<string, JsonElement>? After);

/// <summary>Geri alma sonucu. Başarısızsa Blockers, önce geri alınması gereken sonraki işlemleri listeler.</summary>
public sealed record UndoResult(AuditLog? Log, string? Error, List<string>? Blockers = null);

// ============================================================================================
//  İşlem geçmişi (denetim kaydı) ve geri alma
//
//  Fikir: EF Core'un değişiklik izleyicisi (ChangeTracker), SaveChanges'tan önce hangi satırın
//  eklendiğini, değiştiğini ya da silindiğini ve her alanın eski/yeni değerini zaten biliyor.
//  Kaydetmeden hemen önce ve sonra bu bilgiyi JSON olarak alıp AuditLog'a yazıyoruz.
//  Geri almak = bu listeyi tersine uygulamak.
// ============================================================================================
public partial class AppDb
{
    private const string Added = nameof(EntityState.Added);
    private const string Modified = nameof(EntityState.Modified);
    private const string Deleted = nameof(EntityState.Deleted);

    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter(), new CleanDecimalConverter() },
    };

    /// <summary>
    /// Bekleyen değişiklikleri kaydeder ve AYNI veritabanı işlemi (transaction) içinde her değişen
    /// satırın önceki ve sonraki hâlini AuditLog'a yazar. Ya ikisi birden kaydedilir ya hiçbiri.
    /// Kullanıcı kayıtları (şifre özeti içerdiği için) görüntüye alınmaz.
    /// </summary>
    /// <param name="action">Makine adı, ör. "sale.create".</param>
    /// <param name="summary">Özet metni. Fonksiyon olarak alınır çünkü yeni kayıtların Id'si ancak kaydettikten sonra belli olur.</param>
    public async Task<AuditLog> SaveAuditedAsync(Actor actor, string action, Func<string> summary, bool undoable = true, CancellationToken ct = default)
    {
        // Çağıran zaten bir transaction açtıysa (ör. UndoAsync) onun içinde çalış.
        var ownsTransaction = Database.CurrentTransaction is null;
        await using var tx = ownsTransaction ? await Database.BeginTransactionAsync(ct) : null;

        // 1) Kaydetmeden ÖNCE: neler değişecek ve eski değerleri ne?
        ChangeTracker.DetectChanges();
        var pending = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditLog and not User)
            .Select(e => (Entry: e, e.State, Before: e.State == EntityState.Added ? null : Snapshot(e, original: true)))
            .ToList();

        await SaveChangesAsync(ct);

        // 2) Kaydettikten SONRA: yeni değerler (eklenen satırların Id'leri artık belli).
        var changes = new List<EntityChange>();
        foreach (var p in pending)
        {
            var after = p.State == EntityState.Deleted ? null : Snapshot(p.Entry, original: false);
            if (p.State == EntityState.Modified && SameValues(p.Entry.Metadata, p.Before!, after!)) continue; // gerçekte değişmemiş
            changes.Add(new EntityChange(p.Entry.Metadata.Name, p.State.ToString(), p.Before, after));
        }

        var log = new AuditLog
        {
            At = DateTime.UtcNow,
            UserId = actor.Id,
            UserName = actor.Name,
            Action = action,
            Summary = summary(),
            Changes = JsonSerializer.Serialize(changes, SnapshotJson),
            Undoable = undoable && changes.Count > 0,
        };
        AuditLogs.Add(log);
        await SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return log;
    }

    /// <summary>
    /// Bir işlemi geri alır: eklenenleri siler, silinenleri geri ekler, değişenleri eski değerine döndürür.
    ///
    /// Güvenlik kuralı: etkilenen her satır hâlâ "işlemden hemen sonraki" hâlinde olmalı. Biri sonradan
    /// başka bir işlemle değiştiyse (ör. aynı ürüne ikinci satış) körü körüne geri almak veriyi bozar;
    /// bu durumda reddedip önce geri alınması gereken işlemleri listeleriz.
    ///
    /// Geri alma da geçmişe yeni bir işlem olarak yazılır, yani o da geri alınabilir ("yeniden uygula").
    /// </summary>
    public async Task<UndoResult> UndoAsync(int auditId, Actor actor, CancellationToken ct = default)
    {
        var log = await AuditLogs.FindAsync([auditId], ct);
        if (log is null) return new(null, "Geçmiş kaydı bulunamadı.");
        if (!log.Undoable) return new(null, "Bu işlem geri alınamaz.");
        if (log.UndoneAt is not null) return new(null, "Bu işlem zaten geri alınmış.");

        await using var tx = await Database.BeginTransactionAsync(ct);

        // 1) Her değişiklik için kaydın bugünkü hâlini bul ve çakışma var mı kontrol et.
        var plan = new List<(EntityChange Change, IEntityType Type, object? Current)>();
        var conflicts = new List<(IEntityType Type, object?[] Key)>();
        foreach (var c in ParseChanges(log.Changes))
        {
            var type = Model.FindEntityType(c.Entity);
            if (type is null) return new(null, $"Bilinmeyen kayıt türü: {c.Entity}");
            var key = KeyOf(type, c);
            var current = await FindAsync(type.ClrType, key, ct);
            var unchangedSince = c.State switch
            {
                Added or Modified => current is not null && Matches(Entry(current), type, c.After!),
                Deleted => current is null,
                _ => false,
            };
            if (!unchangedSince) conflicts.Add((type, key));
            plan.Add((c, type, current));
        }

        if (conflicts.Count > 0)
        {
            var blockers = await LaterLogsTouchingAsync(log.Id, conflicts, ct);
            var msg = "Bu işlemden sonra aynı kayıtlar değiştirildiği için doğrudan geri alınamıyor." +
                      (blockers.Count > 0 ? " Önce sonraki işlemleri (en yeniden başlayarak) geri alın." : "");
            return new(null, msg, blockers);
        }

        // 2) Tersine uygula. Kayıtlar genelde üst → alt (satış → kalem) sırasıyla eklenir; geri alırken ters sıra.
        //    Silme kontrollerini SaveChanges anına ertele: EF Core silmeleri yabancı anahtarlara göre kendisi sıralar.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
        plan.Reverse();
        foreach (var (c, type, current) in plan)
        {
            switch (c.State)
            {
                case Added:
                    Remove(current!);
                    break;
                case Deleted:
                    var restored = Activator.CreateInstance(type.ClrType)!;
                    ApplySnapshot(Entry(restored), type, c.Before!);
                    Add(restored);
                    break;
                case Modified:
                    ApplySnapshot(Entry(current!), type, c.Before!);
                    break;
            }
        }

        // 3) Geçmişi işaretle. Bir "geri alma"yı geri almak, asıl işlemi yeniden etkin yapar.
        var isRedo = log.Action == "undo";
        log.UndoneAt = DateTime.UtcNow;
        if (log.UndoOfAuditId is int originalId && await AuditLogs.FindAsync([originalId], ct) is { } original)
        {
            original.UndoneAt = null;
            original.UndoneByAuditId = null;
        }

        var baseSummary = isRedo && log.UndoOfAuditId is int oid
            ? (await AuditLogs.FindAsync([oid], ct))?.Summary ?? log.Summary
            : log.Summary;
        var undoLog = await SaveAuditedAsync(actor, "undo", () => (isRedo ? "Yeniden uygulandı: " : "Geri alındı: ") + StripUndoPrefix(baseSummary), ct: ct);
        undoLog.UndoOfAuditId = log.Id;
        log.UndoneByAuditId = undoLog.Id;
        await SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(undoLog, null);
    }

    public static List<EntityChange> ParseChanges(string json) =>
        JsonSerializer.Deserialize<List<EntityChange>>(json, SnapshotJson) ?? [];

    /// <summary>Geçmiş ekranında gösterilen Türkçe kayıt türü adı.</summary>
    public static string EntityLabel(string entityName) => entityName.Split('.').Last() switch
    {
        nameof(Product) => "Ürün",
        nameof(Supplier) => "Tedarikçi",
        nameof(Sale) => "Satış",
        nameof(SaleLine) => "Satış kalemi",
        nameof(StockMovement) => "Stok hareketi",
        nameof(CashEntry) => "Gelir/gider kaydı",
        nameof(Debt) => "Borç/alacak",
        nameof(DebtPayment) => "Ödeme/tahsilat",
        nameof(CalendarEvent) => "Etkinlik",
        nameof(Setting) => "Ayar",
        var other => other,
    };

    /* ---------------- yardımcılar ---------------- */

    private static Dictionary<string, JsonElement> Snapshot(EntityEntry e, bool original) =>
        e.Properties.ToDictionary(
            p => p.Metadata.Name,
            p => JsonSerializer.SerializeToElement(original ? p.OriginalValue : p.CurrentValue, p.Metadata.ClrType, SnapshotJson));

    private static object? FromJson(JsonElement j, Type t) =>
        j.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize(j, t, SnapshotJson);

    private static bool SameValues(IEntityType type, Dictionary<string, JsonElement> a, Dictionary<string, JsonElement> b) =>
        type.GetProperties().All(p => Equals(FromJson(a[p.Name], p.ClrType), FromJson(b[p.Name], p.ClrType)));

    private static object?[] KeyOf(IEntityType type, EntityChange c) =>
        type.FindPrimaryKey()!.Properties.Select(p => FromJson((c.After ?? c.Before)![p.Name], p.ClrType)).ToArray();

    private static bool Matches(EntityEntry e, IEntityType type, Dictionary<string, JsonElement> snapshot) =>
        type.GetProperties().All(p => !snapshot.TryGetValue(p.Name, out var j) || Equals(e.Property(p.Name).CurrentValue, FromJson(j, p.ClrType)));

    private static void ApplySnapshot(EntityEntry e, IEntityType type, Dictionary<string, JsonElement> snapshot)
    {
        foreach (var p in type.GetProperties())
            if (snapshot.TryGetValue(p.Name, out var j)) e.Property(p.Name).CurrentValue = FromJson(j, p.ClrType);
    }

    private static string StripUndoPrefix(string s)
    {
        foreach (var prefix in new[] { "Geri alındı: ", "Yeniden uygulandı: " })
            if (s.StartsWith(prefix)) return s[prefix.Length..];
        return s;
    }

    /// <summary>Verilen kayıtlara dokunan, henüz geri alınmamış sonraki işlemler (en yeniden eskiye).</summary>
    private async Task<List<string>> LaterLogsTouchingAsync(int afterId, List<(IEntityType Type, object?[] Key)> targets, CancellationToken ct)
    {
        var later = await AuditLogs.AsNoTracking()
            .Where(a => a.Id > afterId && a.UndoneAt == null)
            .OrderByDescending(a => a.Id).Take(1000)
            .ToListAsync(ct);

        var result = new List<string>();
        foreach (var a in later)
        {
            var touches = ParseChanges(a.Changes).Any(c =>
            {
                var type = Model.FindEntityType(c.Entity);
                return type is not null && targets.Any(t => t.Type == type && KeyOf(type, c).SequenceEqual(t.Key));
            });
            if (touches) result.Add($"#{a.Id} · {a.Summary} ({a.UserName}, {a.At.ToLocalTime():dd.MM.yyyy HH:mm})");
        }
        return result;
    }
}
