using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

public class StockTests : ApiTestBase
{
    [Fact]
    public async Task Kapasite_ve_eksi_stok_korunur()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10, capacity: 20);
        Assert.Contains("Kapasite aşılıyor", (await Fails(Post($"/api/products/{pid}/stock-in", new { quantity = 11m }))).Error);
        Assert.Contains("eksiye", (await Fails(Post($"/api/products/{pid}/adjust", new { type = "Fire", quantity = 11m }))).Error);
        await Ok(Post($"/api/products/{pid}/adjust", new { type = "Sayim", quantity = 8m, note = "Ay sonu sayımı" }));
        Assert.Equal(8, await Stock(pid));
    }

    [Fact]
    public async Task Duzeltme_aciklama_ister()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10);
        Assert.Contains("açıklama", (await Fails(Post($"/api/products/{pid}/adjust", new { type = "Duzeltme", quantity = -2m }))).Error);
        await Ok(Post($"/api/products/{pid}/adjust", new { type = "Duzeltme", quantity = -2m, note = "Kırık şişe" }));
        Assert.Equal(8, await Stock(pid));
    }

    [Fact]
    public async Task Urun_duzenlemek_stogu_degistirmez()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10, capacity: 50);
        await Ok(Put($"/api/products/{pid}", new { name = "Çay", category = "İçecek", unit = "adet", stock = 999, capacity = 50, minStock = 2, cost = 60, price = 120, vatRate = 20 }));
        Assert.Equal(10, await Stock(pid));
        Assert.Equal(120, (await Product(pid)).GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task Vadeli_stok_girisi_tedarikciye_borc_yazar()
    {
        await SetupOwner();
        var s = await Ok(Post("/api/suppliers", new { name = "Toptancı" }));
        var sid = s.GetProperty("data").GetProperty("id").GetInt32();
        var pid = await NewProduct(stock: 0, capacity: 100, cost: 10);

        await Ok(Post($"/api/products/{pid}/stock-in", new { quantity = 20m, unitCost = 12m, supplierId = sid, credit = true }));

        var debt = await FirstDebt();
        Assert.Equal(240, debt.GetProperty("amount").GetDecimal());
        Assert.Equal("Borc", debt.GetProperty("kind").GetString());
        Assert.Equal(20, await Stock(pid));
        Assert.Equal(12, (await Product(pid)).GetProperty("cost").GetDecimal()); // son alış fiyatı güncellendi
        Assert.Empty(await CashEntries()); // vadeli: henüz para çıkmadı
    }

    [Fact]
    public async Task Gecmis_tarihteki_stok_hesaplanir()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10);
        await Sell(pid, 3);

        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1).ToString("yyyy-MM-dd");
        var mv = await Ok(Get($"/api/products/{pid}/movements?at={yesterday}"));
        Assert.Equal(0, mv.GetProperty("stockAt").GetDecimal()); // ürün bugün açıldı

        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        mv = await Ok(Get($"/api/products/{pid}/movements?at={today}"));
        Assert.Equal(7, mv.GetProperty("stockAt").GetDecimal());
    }

    [Fact]
    public async Task Hareket_listesi_ture_gore_suzulur()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10);
        await Sell(pid, 1);

        var sales = await Ok(Get("/api/movements?type=Satis"));
        Assert.All(sales.EnumerateArray(), m => Assert.Equal("Satis", m.GetProperty("type").GetString()));
        Assert.Single(sales.EnumerateArray());
        Assert.Contains("Geçersiz", (await Fails(Get("/api/movements?type=Yok"))).Error);
    }
}
