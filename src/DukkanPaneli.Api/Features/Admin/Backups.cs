using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Admin;

public sealed record BackupInfo(string Name, DateTime CreatedAt, long Size);

/// <summary>
/// Yedek alma ve geri yükleme. SQLite'ın <c>VACUUM INTO</c> komutu, uygulama çalışırken bile tutarlı
/// (yarım yazılmamış) bir kopya üretir; dosyayı doğrudan kopyalamak bunu garanti etmez.
/// </summary>
public static class Backups
{
    public const string Prefix = "dukkan-";

    /// <summary>Veritabanı dosyasının yolu (bağlantı dizesindeki Data Source).</summary>
    public static string DatabasePath(AppDb db) => Path.GetFullPath(db.Database.GetDbConnection().DataSource);

    /// <summary>Otomatik yedeklerin klasörü: ayarda yoksa veritabanının yanındaki "backups".</summary>
    public static string Directory(AppDb db, IConfiguration config) =>
        config["Backup:Directory"] is { Length: > 0 } dir
            ? Path.GetFullPath(dir)
            : Path.Combine(Path.GetDirectoryName(DatabasePath(db))!, "backups");

    /// <summary>Veritabanının tutarlı bir kopyasını <paramref name="path"/> dosyasına yazar.</summary>
    public static async Task CreateAsync(AppDb db, string path, CancellationToken ct = default)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Önce geçici dosyaya yaz, bitince adını değiştir: yarıda kesilirse bozuk bir "yedek" kalmasın.
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        await db.Database.ExecuteSqlAsync($"VACUUM INTO {tmp}", ct);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Klasördeki yedekler, en yeniden eskiye.</summary>
    public static List<BackupInfo> List(string dir) =>
        !System.IO.Directory.Exists(dir) ? [] :
        new DirectoryInfo(dir).GetFiles($"{Prefix}*.db")
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .Select(f => new BackupInfo(f.Name, f.LastWriteTimeUtc, f.Length))
            .ToList();

    /// <summary>En yeni <paramref name="keep"/> yedek dışındakileri siler. Silinen dosya sayısını döndürür.</summary>
    public static int Prune(string dir, int keep)
    {
        var old = List(dir).Skip(Math.Max(1, keep)).ToList();
        foreach (var f in old) File.Delete(Path.Combine(dir, f.Name));
        return old.Count;
    }

    /// <summary>Dosyanın gerçekten bu uygulamanın sağlam bir veritabanı olup olmadığını kontrol eder.</summary>
    public static string? Validate(string path)
    {
        if (!File.Exists(path)) return $"Dosya bulunamadı: {path}";
        try
        {
            using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            if (cmd.ExecuteScalar() as string != "ok") return "Yedek dosyası bozuk (integrity_check başarısız).";
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Users', 'Products', 'Sales')";
            if (Convert.ToInt32(cmd.ExecuteScalar()) != 3) return "Bu dosya bir Dükkan Paneli veritabanı değil.";
            return null;
        }
        catch (SqliteException e)
        {
            return $"Dosya bir SQLite veritabanı olarak açılamadı: {e.Message}";
        }
    }

    /// <summary>
    /// Yedeği geri yükler. Uygulama KAPALIYKEN çalıştırılmalıdır (bkz. Infrastructure/Commands.cs).
    /// Mevcut veritabanı silinmez; önce "restore-oncesi" adıyla yedekler klasörüne kopyalanır.
    /// </summary>
    /// <returns>Mevcut verinin kopyalandığı dosya (veritabanı yoksa null).</returns>
    public static async Task<string?> RestoreAsync(AppDb db, string backupPath, string backupDir)
    {
        var dbPath = DatabasePath(db);
        string? safety = null;
        if (File.Exists(dbPath))
        {
            // Uygulama hâlâ çalışıyorsa dosya kilitlidir: hiçbir şey yapmadan IOException ile dur.
            await using (File.Open(dbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

            safety = Path.Combine(backupDir, $"restore-oncesi-{DateTime.Now:yyyy-MM-dd-HHmmss}.db");
            await CreateAsync(db, safety);
        }

        await db.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools(); // dosya kilitlerini bırak
        // -wal ve -shm dosyaları eski veritabanına aittir; kalırlarsa yeni dosyaya uygulanıp onu bozabilirler.
        foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
        File.Copy(backupPath, dbPath);
        return safety;
    }

    public static string FileNameFor(DateOnly day) => $"{Prefix}{day:yyyy-MM-dd}.db";
}

/// <summary>
/// Arka plan servisi: uygulama açıkken her gün bir kez otomatik yedek alır ve eski yedekleri temizler.
/// Ayarlar: <c>Backup:Enabled</c> (varsayılan true), <c>Backup:Keep</c> (kaç gün saklansın, varsayılan 14),
/// <c>Backup:Directory</c> (varsayılan veritabanının yanındaki "backups" klasörü).
/// </summary>
public sealed class AutoBackupService(IServiceScopeFactory scopes, IConfiguration config, ILogger<AutoBackupService> log) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Backup:Enabled", true)) return;
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { log.LogError(e, "Otomatik yedek alınamadı."); } // uygulamayı durdurma, bir saat sonra tekrar dene
            try { await Task.Delay(CheckEvery, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Bugünün yedeği yoksa alır, eskileri siler. Yeni yedek alındıysa yolunu döndürür.</summary>
    public async Task<string?> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        if (!await db.Users.AnyAsync(ct)) return null; // kurulum yapılmamış: yedeklenecek bir şey yok

        var dir = Backups.Directory(db, config);
        var path = Path.Combine(dir, Backups.FileNameFor(Clock.Today));
        if (File.Exists(path)) return null;

        await Backups.CreateAsync(db, path, ct);
        var removed = Backups.Prune(dir, config.GetValue("Backup:Keep", 14));
        log.LogInformation("Otomatik yedek alındı: {Path} ({Removed} eski yedek silindi)", path, removed);
        return path;
    }
}
