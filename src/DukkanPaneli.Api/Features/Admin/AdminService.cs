using System.Globalization;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Admin;

public sealed record SettingsRequest(string? ShopName, decimal? DefaultVatRate);

public sealed record BackupFile(byte[] Content, string FileName);

public sealed record AutoBackupsDto(bool Enabled, int Keep, string Directory, List<BackupInfo> Files);

/// <summary>Ayarlar, örnek veri, veri temizleme ve yedek.</summary>
public sealed class AdminService(AppDb db, DemoDataService demo, IConfiguration config)
{
    /// <summary>Sunucudaki otomatik yedekler (Ayarlar ekranında gösterilir).</summary>
    public AutoBackupsDto AutoBackups()
    {
        var dir = Backups.Directory(db, config);
        return new AutoBackupsDto(config.GetValue("Backup:Enabled", true), config.GetValue("Backup:Keep", 14), dir, Backups.List(dir));
    }

    public async Task<OpResult> UpdateSettingsAsync(Actor actor, SettingsRequest r)
    {
        var name = Input.Clean(r.ShopName, 60);
        if (name.Length == 0) return OpResult.Fail("Dükkan adı boş olamaz.");
        if (r.DefaultVatRate is < 0 or > 100) return OpResult.Fail("KDV oranı 0-100 arasında olmalı.");

        await SetAsync(Setting.ShopName, name);
        if (r.DefaultVatRate is { } vat) await SetAsync(Setting.DefaultVatRate, vat.ToString(CultureInfo.InvariantCulture));
        var log = await db.SaveAuditedAsync(actor, "settings.update", () => $"Ayarlar güncellendi: {name}");
        return OpResult.Ok(log);
    }

    public async Task<OpResult> LoadDemoAsync(Actor actor) => OpResult.Ok(await demo.LoadAsync(actor));

    public async Task<OpResult> ClearAllAsync(Actor actor)
    {
        await demo.ClearAsync();
        var log = await db.SaveAuditedAsync(actor, "data.clear", () => "Tüm işletme verileri silindi", undoable: false);
        return OpResult.Ok(log);
    }

    /// <summary>
    /// Veritabanının tutarlı bir kopyası. SQLite'ın VACUUM INTO komutu, uygulama çalışırken bile
    /// yarım yazılmış veri içermeyen temiz bir kopya üretir (dosyayı doğrudan kopyalamak bunu garanti etmez).
    /// </summary>
    public async Task<BackupFile> BackupAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"dukkan-yedek-{Guid.NewGuid():N}.db");
        try
        {
            await Backups.CreateAsync(db, tmp);
            return new BackupFile(await File.ReadAllBytesAsync(tmp), $"dukkan-yedek-{DateTime.Now:yyyy-MM-dd-HHmm}.db");
        }
        finally
        {
            SqliteConnection.ClearAllPools(); // dosya kilidini bırak, silinebilsin
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private async Task SetAsync(string key, string value)
    {
        var s = await db.Settings.FindAsync(key);
        if (s is null) db.Settings.Add(new Setting { Key = key, Value = value });
        else s.Value = value;
    }
}
