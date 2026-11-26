using System.Net;
using System.Net.Http.Json;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Admin;
using DukkanPaneli.Api.Features.Auth;
using DukkanPaneli.Api.Infrastructure;
using DukkanPaneli.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace DukkanPaneli.Tests.Api;

/// <summary>Sunucu komutları (şifre sıfırlama, yedek, geri yükleme) ve otomatik yedek.</summary>
public sealed class BackupAndResetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dukkan-yedek-test-{Guid.NewGuid():N}");
    private readonly TestApp _app;
    private readonly HttpClient _http;

    public BackupAndResetTests()
    {
        _app = new TestApp(settings: new() { ["Backup:Directory"] = _dir, ["Backup:Keep"] = "3" });
        _http = _app.Client();
    }

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* klasör yoksa ya da kilitliyse sorun değil */ }
    }

    private Task Setup() => _http.PostAsJsonAsync("/api/auth/setup",
        new { shopName = "Test", userName = "sahip", password = "gizli-sifre-1", loadDemo = false });

    private async Task<(int Code, string Output)> Run(params string[] args)
    {
        var output = new StringWriter();
        var code = await Commands.RunAsync(_app.Services, args, output);
        return (code, output.ToString());
    }

    private async Task<HttpStatusCode> Login(string password)
    {
        var c = _app.Client();
        return (await c.PostAsJsonAsync("/api/auth/login", new { userName = "sahip", password })).StatusCode;
    }

    private async Task AddProduct(string name)
    {
        var r = await _http.PostAsJsonAsync("/api/products", new { name, category = "", unit = "adet", stock = 1, capacity = 10, minStock = 0, cost = 1, price = 2, vatRate = 20 });
        r.EnsureSuccessStatusCode();
    }

    private async Task<List<string>> ProductNames()
    {
        var boot = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/bootstrap");
        return [.. boot.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("name").GetString()!)];
    }

    [Fact]
    public async Task Sifre_sifirlama_gecici_sifre_verir_ve_eski_oturumlari_kapatir()
    {
        await Setup();
        var (code, output) = await Run("reset-password", "SAHIP");
        Assert.Equal(0, code);
        var temp = output.Split(": ")[1].Split(Environment.NewLine)[0].Trim();
        Assert.Equal(12, temp.Length);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/bootstrap")).StatusCode); // eski oturum düştü
        Assert.Equal(HttpStatusCode.Unauthorized, await Login("gizli-sifre-1"));
        Assert.Equal(HttpStatusCode.OK, await Login(temp));
    }

    [Fact]
    public async Task Olmayan_kullanici_icin_sifirlama_hata_verir()
    {
        await Setup();
        var (code, output) = await Run("reset-password", "yok-boyle-biri");
        Assert.Equal(1, code);
        Assert.Contains("bulunamadı", output);
    }

    [Fact]
    public async Task Otomatik_yedek_gunde_bir_kez_alinir_ve_eskiler_silinir()
    {
        await Setup();
        Directory.CreateDirectory(_dir);
        for (var i = 1; i <= 5; i++) await File.WriteAllTextAsync(Path.Combine(_dir, $"dukkan-2020-01-0{i}.db"), "eski");

        var svc = _app.Services.GetRequiredService<AutoBackupService>();
        var path = await svc.RunOnceAsync();
        Assert.Equal(Path.Combine(_dir, Backups.FileNameFor(Clock.Today)), path);
        Assert.Null(await svc.RunOnceAsync()); // bugünün yedeği zaten var

        var names = Backups.List(_dir).Select(f => f.Name).ToList();
        Assert.Equal([Backups.FileNameFor(Clock.Today), "dukkan-2020-01-05.db", "dukkan-2020-01-04.db"], names);
        Assert.Null(Backups.Validate(path!)); // alınan yedek sağlam bir veritabanı

        var list = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/backups");
        Assert.Equal(3, list.GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task Yedek_geri_yuklenir_ve_mevcut_veri_once_kopyalanir()
    {
        await Setup();
        await AddProduct("Çay");
        var (code, output) = await Run("backup");
        Assert.Equal(0, code);
        var backup = Backups.List(_dir).Single().Name;

        await AddProduct("Kahve");
        Assert.Equal(["Çay", "Kahve"], (await ProductNames()).Order());

        (code, output) = await Run("restore-backup", backup);
        Assert.True(code == 0, output);
        Assert.Contains("restore-oncesi-", output);

        Assert.Equal(["Çay"], await ProductNames());
        Assert.Single(Directory.GetFiles(_dir, "restore-oncesi-*.db"));
    }

    [Fact]
    public async Task Bozuk_ya_da_yabanci_dosya_geri_yuklenmez()
    {
        await Setup();
        Directory.CreateDirectory(_dir);
        var junk = Path.Combine(_dir, "dukkan-bozuk.db");
        await File.WriteAllTextAsync(junk, "bu bir veritabanı değil");

        var (code, output) = await Run("restore-backup", junk);
        Assert.Equal(1, code);
        Assert.Contains("HATA", output);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/bootstrap")).StatusCode); // veri yerinde
    }

    [Fact]
    public void Komut_olmayan_argumanlar_web_sunucusunu_baslatir()
    {
        Assert.False(Commands.IsCommand([]));
        Assert.False(Commands.IsCommand(["--urls", "http://localhost:5080"]));
        Assert.True(Commands.IsCommand(["reset-password", "sahip"]));
    }
}
