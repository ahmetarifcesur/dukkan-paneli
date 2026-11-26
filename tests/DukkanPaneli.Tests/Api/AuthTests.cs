using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

public class AuthTests : ApiTestBase
{
    [Fact]
    public async Task Kurulum_giris_ve_yetkiler()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Fails(Get("/api/bootstrap"))).Code);

        await SetupOwner();
        var again = await Fails(Post("/api/auth/setup", new { shopName = "X", userName = "baska", password = "12345678" }));
        Assert.Equal(HttpStatusCode.Conflict, again.Code);

        await Ok(Post("/api/users", new { userName = "calisan1", displayName = "Ayşe", role = "Calisan", password = "calisan-sifre" }));
        await Ok(Post("/api/auth/logout"));

        var bad = await Fails(Post("/api/auth/login", new { userName = "calisan1", password = "yanlis-sifre" }));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Code);

        await Ok(Post("/api/auth/login", new { userName = "calisan1", password = "calisan-sifre" }));
        var boot = await Bootstrap();
        Assert.Equal(JsonValueKind.Null, boot.GetProperty("cash").ValueKind); // çalışan finansı görmez
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(Get("/api/history"))).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(Get("/api/reports"))).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(Post("/api/cash", new { type = "Gelir", amount = 5 }))).Code);
    }

    [Fact]
    public async Task Ozel_baslik_olmadan_degisiklik_reddedilir()
    {
        await SetupOwner();
        var raw = App.CreateClient(); // X-Requested-With başlığı yok (başka bir siteden gelen form gibi)
        var r = await raw.PostAsJsonAsync("/api/auth/login", new { userName = "sahip", password = "gizli-sifre-1" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Devre_disi_birakilan_kullanicinin_oturumu_hemen_kapanir()
    {
        await SetupOwner();
        var u = await Ok(Post("/api/users", new { userName = "gecici", displayName = "Geçici", role = "Calisan", password = "gecici-sifre" }));
        var uid = u.GetProperty("data").GetProperty("id").GetInt32();

        var staff = App.Client();
        await Ok(staff.PostAsJsonAsync("/api/auth/login", new { userName = "gecici", password = "gecici-sifre" }));
        await Ok(staff.GetAsync("/api/bootstrap"));

        await Ok(Put($"/api/users/{uid}", new { displayName = "Geçici", role = "Calisan", isActive = false }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/bootstrap")).StatusCode);
    }

    [Fact]
    public async Task Son_sahip_calisana_cevrilemez()
    {
        await SetupOwner();
        var me = (await Bootstrap()).GetProperty("me").GetProperty("id").GetInt32();
        var (_, err) = await Fails(Put($"/api/users/{me}", new { displayName = "Ahmet", role = "Calisan", isActive = true }));
        Assert.Contains("en az bir aktif sahip", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sifre_degisince_eski_sifreyle_giris_yapilamaz()
    {
        await SetupOwner();
        Assert.Contains("Mevcut şifre hatalı", (await Fails(Post("/api/auth/change-password", new { current = "yanlis", next = "yeni-sifre-2" }))).Error);
        await Ok(Post("/api/auth/change-password", new { current = "gizli-sifre-1", next = "yeni-sifre-2" }));
        await Ok(Post("/api/auth/logout"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Fails(Post("/api/auth/login", new { userName = "sahip", password = "gizli-sifre-1" }))).Code);
        await Ok(Post("/api/auth/login", new { userName = "sahip", password = "yeni-sifre-2" }));
    }
}
