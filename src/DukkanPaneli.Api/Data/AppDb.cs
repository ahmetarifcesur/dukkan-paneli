using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DukkanPaneli.Api.Data;

/// <summary>SQLite'ta decimal yerel tip değildir; 4 basamak hassasiyetle tamsayı saklıyoruz (karşılaştırma/sıralama SQL'de doğru çalışır).</summary>
public sealed class ScaledDecimalConverter() : ValueConverter<decimal, long>(v => (long)Math.Round(v * 10000m), v => v / 10000m);

/// <summary>Zaman damgaları UTC saklanır; SQLite türü kaybettiği için okurken UTC olarak işaretliyoruz (aksi hâlde istemci yerel saat sanar).</summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

/// <summary>İşlemi yapan kullanıcı.</summary>
public sealed record Actor(int? Id, string Name, Role Role)
{
    public bool IsOwner => Role == Role.Sahip;
    public static readonly Actor System = new(null, "Sistem", Role.Sahip);
}

/// <summary>Tek bir varlığın değişikliği: Added / Modified / Deleted ve önce/sonra değerleri.</summary>
public sealed record EntityChange(string Entity, string State, Dictionary<string, JsonElement>? Before, Dictionary<string, JsonElement>? After);

public sealed record UndoResult(AuditLog? Log, string? Error, List<string>? Blockers = null);

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashEntry> CashEntries => Set<CashEntry>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HaveConversion<ScaledDecimalConverter>();
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        b.Properties<Role>().HaveConversion<string>();
        b.Properties<CashType>().HaveConversion<string>();
        b.Properties<PaymentType>().HaveConversion<string>();
        b.Properties<DebtKind>().HaveConversion<string>();
        b.Properties<MovementType>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict;
        m.Entity<User>().HasIndex(x => x.UserName).IsUnique();
        m.Entity<Setting>().HasKey(x => x.Key);
        m.Entity<Product>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
        m.Entity<Sale>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(R);
        m.Entity<Sale>().HasIndex(x => x.Date);
        m.Entity<SaleLine>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
        m.Entity<StockMovement>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(R);
        m.Entity<StockMovement>().HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(R);
        m.Entity<StockMovement>().HasIndex(x => new { x.ProductId, x.Date });
        m.Entity<CashEntry>().HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(R);
        m.Entity<CashEntry>().HasOne(x => x.DebtPayment).WithMany().HasForeignKey(x => x.DebtPaymentId).OnDelete(R);
        m.Entity<CashEntry>().HasOne(x => x.StockMovement).WithMany().HasForeignKey(x => x.StockMovementId).OnDelete(R);
        m.Entity<CashEntry>().HasIndex(x => x.Date);
        m.Entity<Debt>().HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.DebtId).OnDelete(R);
        m.Entity<Debt>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(R);
        m.Entity<Debt>().HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(R);
        m.Entity<Debt>().HasOne(x => x.StockMovement).WithMany().HasForeignKey(x => x.StockMovementId).OnDelete(R);
        m.Entity<CalendarEvent>().HasIndex(x => x.Date);
        m.Entity<AuditLog>().HasIndex(x => x.At);
    }

    /* =================== Denetim kaydı (hareket geçmişi) =================== */

    private static readonly JsonSerializerOptions SnapOpts = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter(), new DukkanPaneli.Api.Infrastructure.CleanDecimalConverter() },
    };

    private static Dictionary<string, JsonElement> Snap(EntityEntry e, bool original) =>
        e.Properties.ToDictionary(p => p.Metadata.Name,
            p => JsonSerializer.SerializeToElement(original ? p.OriginalValue : p.CurrentValue, p.Metadata.ClrType, SnapOpts));

    private static object? FromJson(JsonElement j, Type t) =>
        j.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize(j, t, SnapOpts);

    public static List<EntityChange> ParseChanges(string json) =>
        JsonSerializer.Deserialize<List<EntityChange>>(json, SnapOpts) ?? [];

    /// <summary>
    /// Bekleyen değişiklikleri kaydeder ve aynı veritabanı işlemi içinde her değişen satırın
    /// önceki ve sonraki hâlini AuditLog'a yazar. Kullanıcılar (şifre özeti içerdiği için) görüntüye alınmaz.
    /// </summary>
    public async Task<AuditLog> SaveAuditedAsync(Actor actor, string action, Func<string> summary, bool undoable = true, CancellationToken ct = default)
    {
        var ownTx = Database.CurrentTransaction is null;
        await using var tx = ownTx ? await Database.BeginTransactionAsync(ct) : null;

        ChangeTracker.DetectChanges();
        var pending = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && e.Entity is not AuditLog and not User)
            .Select(e => (Entry: e, e.State, Before: e.State == EntityState.Added ? null : Snap(e, true)))
            .ToList();

        await SaveChangesAsync(ct);

        var changes = new List<EntityChange>();
        foreach (var p in pending)
        {
            var after = p.State == EntityState.Deleted ? null : Snap(p.Entry, false);
            if (p.State == EntityState.Modified && SameValues(p.Entry.Metadata, p.Before!, after!)) continue;
            changes.Add(new EntityChange(p.Entry.Metadata.Name, p.State.ToString(), p.Before, after));
        }

        var log = new AuditLog
        {
            At = DateTime.UtcNow,
            UserId = actor.Id,
            UserName = actor.Name,
            Action = action,
            Summary = summary(),
            Changes = JsonSerializer.Serialize(changes, SnapOpts),
            Undoable = undoable && changes.Count > 0,
        };
        AuditLogs.Add(log);
        await SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return log;
    }

    private static bool SameValues(IEntityType et, Dictionary<string, JsonElement> a, Dictionary<string, JsonElement> b) =>
        et.GetProperties().All(p => Equals(FromJson(a[p.Name], p.ClrType), FromJson(b[p.Name], p.ClrType)));

    private static object?[] KeyOf(IEntityType et, EntityChange c) =>
        et.FindPrimaryKey()!.Properties.Select(p => FromJson((c.After ?? c.Before)![p.Name], p.ClrType)).ToArray();

    private static bool Matches(EntityEntry e, IEntityType et, Dictionary<string, JsonElement> snap) =>
        et.GetProperties().All(p => !snap.TryGetValue(p.Name, out var j) || Equals(e.Property(p.Name).CurrentValue, FromJson(j, p.ClrType)));

    private static void Apply(EntityEntry e, IEntityType et, Dictionary<string, JsonElement> snap)
    {
        foreach (var p in et.GetProperties())
            if (snap.TryGetValue(p.Name, out var j)) e.Property(p.Name).CurrentValue = FromJson(j, p.ClrType);
    }

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

    /// <summary>
    /// Bir işlemi geri alır: eklenenleri siler, silinenleri geri ekler, değişenleri eski değerine döndürür.
    /// Etkilenen satırlardan biri sonradan başka bir işlemle değiştiyse veri bozulmasın diye reddeder
    /// ve önce geri alınması gereken işlemleri listeler. Geri alma işlemi de geri alınabilir (yeniden uygula).
    /// </summary>
    public async Task<UndoResult> UndoAsync(int auditId, Actor actor, CancellationToken ct = default)
    {
        var log = await AuditLogs.FindAsync([auditId], ct);
        if (log is null) return new(null, "Geçmiş kaydı bulunamadı.");
        if (!log.Undoable) return new(null, "Bu işlem geri alınamaz.");
        if (log.UndoneAt is not null) return new(null, "Bu işlem zaten geri alınmış.");

        var changes = ParseChanges(log.Changes);
        await using var tx = await Database.BeginTransactionAsync(ct);

        var plan = new List<(EntityChange Change, IEntityType Type, object? Current)>();
        var conflicted = new List<(IEntityType Type, object?[] Key)>();
        foreach (var c in changes)
        {
            var et = Model.FindEntityType(c.Entity);
            if (et is null) return new(null, $"Bilinmeyen kayıt türü: {c.Entity}");
            var key = KeyOf(et, c);
            var current = await FindAsync(et.ClrType, key, ct);
            var ok = c.State switch
            {
                "Added" or "Modified" => current is not null && Matches(Entry(current), et, c.After!),
                "Deleted" => current is null,
                _ => false,
            };
            if (!ok) conflicted.Add((et, key));
            plan.Add((c, et, current));
        }

        if (conflicted.Count > 0)
        {
            var blockers = await LaterLogsTouching(log.Id, conflicted, ct);
            var msg = "Bu işlemden sonra aynı kayıtlar değiştirildiği için doğrudan geri alınamıyor." +
                      (blockers.Count > 0 ? " Önce sonraki işlemleri (en yeniden başlayarak) geri alın." : "");
            return new(null, msg, blockers);
        }

        // Kayıtlar genelde önce üst (satış), sonra alt (kalem) sırasıyla eklenir; geri alırken ters sırada işle.
        // Silme kontrollerini SaveChanges anına ertele: EF Core silmeleri yabancı anahtarlara göre kendisi sıralar.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
        plan.Reverse();
        foreach (var (c, et, current) in plan)
        {
            switch (c.State)
            {
                case "Added":
                    Remove(current!);
                    break;
                case "Deleted":
                    var obj = Activator.CreateInstance(et.ClrType)!;
                    Apply(Entry(obj), et, c.Before!);
                    Add(obj);
                    break;
                case "Modified":
                    Apply(Entry(current!), et, c.Before!);
                    break;
            }
        }

        var isRedo = log.Action == "undo";
        log.UndoneAt = DateTime.UtcNow;
        if (log.UndoOfAuditId is int origId && await AuditLogs.FindAsync([origId], ct) is { } orig)
        {
            orig.UndoneAt = null;
            orig.UndoneByAuditId = null;
        }

        var original = isRedo && log.UndoOfAuditId is int oid ? (await AuditLogs.FindAsync([oid], ct))?.Summary ?? log.Summary : log.Summary;
        var undoLog = await SaveAuditedAsync(actor, "undo", () => (isRedo ? "Yeniden uygulandı: " : "Geri alındı: ") + StripPrefix(original), ct: ct);
        undoLog.UndoOfAuditId = log.Id;
        log.UndoneByAuditId = undoLog.Id;
        await SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(undoLog, null);
    }

    private static string StripPrefix(string s) =>
        s.StartsWith("Geri alındı: ") ? s["Geri alındı: ".Length..] : s.StartsWith("Yeniden uygulandı: ") ? s["Yeniden uygulandı: ".Length..] : s;

    private async Task<List<string>> LaterLogsTouching(int afterId, List<(IEntityType Type, object?[] Key)> targets, CancellationToken ct)
    {
        var later = await AuditLogs.AsNoTracking().Where(a => a.Id > afterId && a.UndoneAt == null)
            .OrderByDescending(a => a.Id).Take(1000).ToListAsync(ct);
        var result = new List<string>();
        foreach (var a in later)
        {
            var touches = ParseChanges(a.Changes).Any(c =>
            {
                var et = Model.FindEntityType(c.Entity);
                return et is not null && targets.Any(t => t.Type == et && KeyOf(et, c).SequenceEqual(t.Key));
            });
            if (touches) result.Add($"#{a.Id} · {a.Summary} ({a.UserName}, {a.At.ToLocalTime():dd.MM.yyyy HH:mm})");
        }
        return result;
    }
}
