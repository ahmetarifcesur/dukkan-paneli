using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

/// <summary>Çalışanlar sayfası: kullanıcı başına satış özeti ve son giriş.</summary>
public class StaffTests : ApiTestBase
{
    private async Task<JsonElement> UserRow(string userName) =>
        (await Ok(Get("/api/users"))).EnumerateArray().Single(u => u.GetProperty("userName").GetString() == userName);

    private static (int Count, decimal Total) Stats(JsonElement user, string period) =>
        (user.GetProperty(period).GetProperty("count").GetInt32(), user.GetProperty(period).GetProperty("total").GetDecimal());

    [Fact]
    public async Task Satislar_kullaniciya_gore_sayilir_iptaller_haric()
    {
        await SetupOwner();
        var productId = await NewProduct(stock: 20, price: 100);
        var staff = await StaffClient();

        await Ok(staff.PostAsJsonAsync("/api/sales", new { paymentType = "Nakit", lines = new[] { new { productId, quantity = 2 } } }));
        var second = await Ok(staff.PostAsJsonAsync("/api/sales", new { paymentType = "Kart", lines = new[] { new { productId, quantity = 1 } } }));
        await Sell(productId, 3); // sahibin satışı

        // Çalışanın ikinci satışını sahip iptal ediyor: istatistikten düşmeli.
        var cancelledId = second.GetProperty("data").GetProperty("saleId").GetInt32();
        await Ok(Post($"/api/sales/{cancelledId}/cancel", new { reason = "yanlış" }));

        var kasa = await UserRow("kasa");
        Assert.Equal((1, 200m), Stats(kasa, "today"));
        Assert.Equal((1, 200m), Stats(kasa, "month"));
        Assert.NotEqual(JsonValueKind.Null, kasa.GetProperty("lastLoginAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, kasa.GetProperty("lastSaleAt").ValueKind);

        var sahip = await UserRow("sahip");
        Assert.Equal((1, 300m), Stats(sahip, "today"));
    }

    [Fact]
    public async Task Ad_degisince_satislar_ayni_kullanicida_kalir()
    {
        await SetupOwner();
        var productId = await NewProduct(price: 50);
        var staff = await StaffClient();
        await Ok(staff.PostAsJsonAsync("/api/sales", new { paymentType = "Nakit", lines = new[] { new { productId, quantity = 1 } } }));

        var id = (await UserRow("kasa")).GetProperty("id").GetInt32();
        await Ok(Put($"/api/users/{id}", new { displayName = "Yeni Ad", role = "Calisan", isActive = true }));

        var kasa = await UserRow("kasa");
        Assert.Equal("Yeni Ad", kasa.GetProperty("displayName").GetString());
        Assert.Equal((1, 50m), Stats(kasa, "month"));
    }

    [Fact]
    public async Task Hic_giris_yapmamis_kullanicinin_son_girisi_bostur()
    {
        await SetupOwner();
        await Ok(Post("/api/users", new { userName = "yeni", displayName = "Yeni", role = "Calisan", password = "yeni-sifre-1" }));
        var u = await UserRow("yeni");
        Assert.Equal(JsonValueKind.Null, u.GetProperty("lastLoginAt").ValueKind);
        Assert.Equal((0, 0m), Stats(u, "month"));
    }

    [Fact]
    public async Task Calisan_kullanici_listesini_goremez()
    {
        await SetupOwner();
        var staff = await StaffClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/users")).StatusCode);
    }
}
