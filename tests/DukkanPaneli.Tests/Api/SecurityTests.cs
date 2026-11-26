using System.Net;
using System.Net.Http.Json;
using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

/// <summary>Yayına çıkmadan önce kapatılan açıkların testleri.</summary>
public class SecurityTests : ApiTestBase
{
    private static object SetupBody(string? token = null) =>
        new { shopName = "Test", userName = "sahip", password = "gizli-sifre-1", loadDemo = false, setupToken = token };

    [Fact]
    public async Task Calisanin_gonderdigi_fiyat_yok_sayilir()
    {
        await SetupOwner();
        var productId = await NewProduct(price: 100);
        var staff = await StaffClient();

        // Arayüz çalışan için fiyat göndermez; API'yi doğrudan çağırıp 1 TL'ye satmayı deniyoruz.
        var r = await Ok(staff.PostAsJsonAsync("/api/sales",
            new { paymentType = "Nakit", lines = new[] { new { productId, quantity = 1, unitPrice = 1 } } }));
        var saleId = r.GetProperty("data").GetProperty("saleId").GetInt32();

        var sale = await Ok(Get($"/api/sales/{saleId}"));
        Assert.Equal(100m, sale.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Sahip_satista_fiyati_degistirebilir()
    {
        await SetupOwner();
        var productId = await NewProduct(price: 100);
        var r = await Ok(Post("/api/sales", new { paymentType = "Nakit", lines = new[] { new { productId, quantity = 1, unitPrice = 80 } } }));
        var sale = await Ok(Get($"/api/sales/{r.GetProperty("data").GetProperty("saleId").GetInt32()}"));
        Assert.Equal(80m, sale.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Kurulum_anahtari_ayarliysa_zorunludur()
    {
        using var app = new TestApp(settings: new() { ["Setup:Token"] = "dogru-anahtar" });
        var http = app.Client();

        var status = await Ok(http.GetAsync("/api/auth/status"));
        Assert.True(status.GetProperty("setupTokenRequired").GetBoolean());

        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(http.PostAsJsonAsync("/api/auth/setup", SetupBody()))).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(http.PostAsJsonAsync("/api/auth/setup", SetupBody("yanlis")))).Code);
        await Ok(http.PostAsJsonAsync("/api/auth/setup", SetupBody("dogru-anahtar")));
    }

    [Fact]
    public async Task Yayinda_anahtarsiz_kurulum_disaridan_yapilamaz()
    {
        // Test sunucusunda istek "uzaktan" gelir (IP yok, localhost değil).
        using var app = new TestApp(environment: "Production");
        var http = app.Client();
        var (code, error) = await Fails(http.PostAsJsonAsync("/api/auth/setup", SetupBody()));
        Assert.Equal(HttpStatusCode.Forbidden, code);
        Assert.Contains("Setup__Token", error);
    }

    [Fact]
    public async Task Guvenlik_basliklari_gonderilir()
    {
        var r = await Get("/api/auth/status");
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("object-src 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(r.Headers.Contains("Permissions-Policy"));
    }
}
