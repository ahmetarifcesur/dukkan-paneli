using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

/// <summary>Kod incelemesinde bulunan hataların testleri (her biri önce başarısız olan bir senaryo).</summary>
public class ReviewFixTests : ApiTestBase
{
    private static string Today => DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
    private static string DaysAgo(int n) => DateOnly.FromDateTime(DateTime.Now).AddDays(-n).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Calisan_stok_girisinde_maliyet_ve_vadeli_alim_giremez()
    {
        await SetupOwner();
        var productId = await NewProduct(stock: 1, capacity: 50, cost: 60);
        var supplier = await Ok(Post("/api/suppliers", new { name = "Toptancı" }));
        var supplierId = supplier.GetProperty("data").GetProperty("id").GetInt32();
        var staff = await StaffClient();

        // Gönderdiği maliyet yok sayılır: ürünün maliyeti değişmez, gider kayıtlı maliyetle yazılır.
        await Ok(staff.PostAsJsonAsync($"/api/products/{productId}/stock-in", new { quantity = 2, unitCost = 1 }));
        Assert.Equal(60m, (await Product(productId)).GetProperty("cost").GetDecimal());
        var expense = (await CashEntries()).First(c => c.GetProperty("category").GetString() == "Tedarik");
        Assert.Equal(120m, expense.GetProperty("amount").GetDecimal());

        var credit = await staff.PostAsJsonAsync($"/api/products/{productId}/stock-in", new { quantity = 1, supplierId, credit = true });
        Assert.Equal(HttpStatusCode.Forbidden, credit.StatusCode);
    }

    [Fact]
    public async Task Calisan_stok_hareketini_gecmis_tarihe_giremez_kimse_ileri_tarihe_giremez()
    {
        await SetupOwner();
        var productId = await NewProduct(stock: 5);
        var staff = await StaffClient();

        var past = await staff.PostAsJsonAsync($"/api/products/{productId}/adjust", new { type = "Fire", quantity = 1, date = DaysAgo(3) });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        await Ok(staff.PostAsJsonAsync($"/api/products/{productId}/adjust", new { type = "Fire", quantity = 1, date = Today }));

        // Sahip geçmişe girebilir ama ileriye giremez (satışta da aynı kural).
        await Ok(Post($"/api/products/{productId}/adjust", new { type = "Fire", quantity = 1, date = DaysAgo(3) }));
        var tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1).ToString("yyyy-MM-dd");
        Assert.Contains("İleri", (await Fails(Post($"/api/products/{productId}/stock-in", new { quantity = 1, date = tomorrow }))).Error);
        Assert.Contains("İleri", (await Fails(Post("/api/sales", new { paymentType = "Nakit", date = tomorrow, lines = new[] { new { productId, quantity = 1 } } }))).Error);
    }

    [Fact]
    public async Task Odemesi_olan_kaydin_turu_degistirilemez()
    {
        await SetupOwner();
        var created = await Ok(Post("/api/debts", new { kind = "Borc", party = "Banka", amount = 1000m }));
        var id = created.GetProperty("data").GetProperty("id").GetInt32();
        await Ok(Post($"/api/debts/{id}/payments", new { amount = 100m, record = true }));

        var (_, error) = await Fails(Put($"/api/debts/{id}", new { kind = "Alacak", party = "Banka", amount = 1000m }));
        Assert.Contains("türü", error);
        await Ok(Put($"/api/debts/{id}", new { kind = "Borc", party = "Banka A.Ş.", amount = 1000m })); // tür aynıysa sorun yok
    }

    [Theory]
    [InlineData("Çay", "ÇAY")]
    [InlineData("Işık Gıda", "IŞIK GIDA")]
    [InlineData("şeker", "Şeker")]
    public async Task Ayni_isim_Turkce_buyuk_kucuk_harfle_tekrar_eklenemez(string first, string second)
    {
        await SetupOwner();
        await NewProduct(name: first);
        Assert.Contains("zaten var", (await Fails(Post("/api/products",
            new { name = second, category = "", unit = "adet", stock = 0, capacity = 10, minStock = 0, cost = 1, price = 2, vatRate = 20 }))).Error);

        await Ok(Post("/api/suppliers", new { name = first }));
        Assert.Contains("zaten var", (await Fails(Post("/api/suppliers", new { name = second }))).Error);
    }

    [Fact]
    public async Task Acilista_sadece_son_12_ayin_gelir_gideri_gelir_eskisi_istenince_gelir()
    {
        await SetupOwner();
        var old = DateOnly.FromDateTime(DateTime.Now).AddYears(-2).ToString("yyyy-MM-dd");
        await Ok(Post("/api/cash", new { type = "Gider", amount = 10m, category = "Kira", date = old }));
        await Ok(Post("/api/cash", new { type = "Gider", amount = 20m, category = "Kira", date = Today }));

        var boot = await Bootstrap();
        Assert.Single(boot.GetProperty("cash").EnumerateArray());
        Assert.True(string.CompareOrdinal(boot.GetProperty("cashFrom").GetString(), old) > 0);

        var wider = await Ok(Get($"/api/bootstrap?cashFrom={old}"));
        Assert.Equal(2, wider.GetProperty("cash").GetArrayLength());
        Assert.Equal(old, wider.GetProperty("cashFrom").GetString());
    }
}
