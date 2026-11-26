using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Admin;
using DukkanPaneli.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Infrastructure;

/// <summary>
/// Sunucuda, web arayüzü olmadan çalıştırılan bakım komutları. Örnek:
/// <code>
/// dotnet run --project src/DukkanPaneli.Api -- reset-password sahip
/// dotnet run --project src/DukkanPaneli.Api -- backup
/// dotnet run --project src/DukkanPaneli.Api -- list-backups
/// dotnet run --project src/DukkanPaneli.Api -- restore-backup dukkan-2026-11-20.db
/// </code>
/// Bu komutlar sunucuya erişimi olan kişi içindir; bu yüzden web'den çağrılamazlar.
/// </summary>
public static class Commands
{
    private static readonly Actor ServerActor = new(null, "Sunucu komutu", Role.Owner);

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] is "reset-password" or "backup" or "list-backups" or "restore-backup" or "help";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDb>();
        var config = sp.GetRequiredService<IConfiguration>();
        var dir = Backups.Directory(db, config);

        switch (args)
        {
            case ["reset-password", var userName]:
            {
                await db.Database.MigrateAsync();
                var r = await sp.GetRequiredService<UserService>().ResetPasswordAsync(ServerActor, userName);
                if (!r.Succeeded) return Error(output, r.Error!);
                output.WriteLine($"'{AuthService.NormalizeUserName(userName)}' için geçici şifre: {r.Value}");
                output.WriteLine("Bu şifreyle giriş yapıp Ayarlar > Hesabım'dan hemen yeni bir şifre belirleyin.");
                return 0;
            }

            case ["backup"]:
            {
                await db.Database.MigrateAsync();
                var path = Path.Combine(dir, $"{Backups.Prefix}{DateTime.Now:yyyy-MM-dd-HHmmss}.db");
                await Backups.CreateAsync(db, path);
                output.WriteLine($"Yedek alındı: {path}");
                return 0;
            }

            case ["list-backups"]:
            {
                var files = Backups.List(dir);
                output.WriteLine($"Yedek klasörü: {dir}");
                if (files.Count == 0) output.WriteLine("Henüz yedek yok.");
                foreach (var f in files)
                    output.WriteLine($"  {f.Name}  {f.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}  {f.Size / 1024.0:0} KB");
                return 0;
            }

            case ["restore-backup", var file]:
            {
                // Sadece dosya adı verildiyse yedek klasöründe ara.
                var path = Path.IsPathRooted(file) || File.Exists(file) ? Path.GetFullPath(file) : Path.Combine(dir, file);
                if (Backups.Validate(path) is { } error) return Error(output, error);
                try
                {
                    var safety = await Backups.RestoreAsync(db, path, dir);
                    if (safety is not null) output.WriteLine($"Mevcut veriler şuraya kopyalandı: {safety}");
                    output.WriteLine($"Geri yüklendi: {path}");
                    output.WriteLine("Uygulamayı yeniden başlatın. Eski bir yedekse şema güncellemesi açılışta otomatik yapılır.");
                    return 0;
                }
                catch (IOException e)
                {
                    return Error(output, $"Veritabanı dosyası kullanımda; önce uygulamayı durdurun. ({e.Message})");
                }
            }

            default:
                output.WriteLine("""
                    Komutlar:
                      reset-password <kullanıcı-adı>   Geçici şifre üretir (unutulan şifre için)
                      backup                           Şimdi bir yedek alır
                      list-backups                     Yedekleri listeler
                      restore-backup <dosya>           Yedeği geri yükler (önce uygulamayı durdurun)
                    """);
                return args is ["help"] ? 0 : 1;
        }
    }

    private static int Error(TextWriter output, string message)
    {
        output.WriteLine("HATA: " + message);
        return 1;
    }
}
