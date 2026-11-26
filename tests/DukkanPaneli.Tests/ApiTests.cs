using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DukkanPaneli.Tests;

/// <summary>Her test kendi geçici SQLite veritabanıyla gerçek HTTP uç noktalarını çağırır.</summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"dukkan-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_db};Pooling=False");

    public HttpClient Client()
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("X-Requested-With", "dukkan");
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_db); } catch { /* yoksay */ }
    }
}

public class ApiTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly HttpClient _c;

    public ApiTests() => _c = _app.Client();
    public void Dispose() => _app.Dispose();

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var r = await call;
        var body = await Json(r);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body;
    }

    private async Task<(HttpStatusCode Code, string Error)> Fails(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.False(r.IsSuccessStatusCode, "İstek başarısız olmalıydı.");
        var body = await Json(r);
        return (r.StatusCode, body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) ? e.GetString()! : "");
    }

    private Task<HttpResponseMessage> Post(string url, object? body = null) => _c.PostAsJsonAsync(url, body ?? new { });
    private Task<HttpResponseMessage> Put(string url, object body) => _c.PutAsJsonAsync(url, body);

    private async Task Setup(bool demo = false) =>
        await Ok(Post("/api/auth/setup", new { shopName = "Test Dükkan", displayName = "Ahmet", userName = "sahip", password = "gizli-sifre-1", loadDemo = demo }));

    private async Task<int> NewProduct(string name = "Çay", decimal stock = 10, decimal capacity = 50, decimal price = 100, decimal cost = 60)
    {
        var r = await Ok(Post("/api/products", new { name, category = "İçecek", unit = "adet", stock, capacity, minStock = 2, cost, price, vatRate = 20 }));
        return r.GetProperty("data").GetProperty("id").GetInt32();
    }

    private async Task<JsonElement> Product(int id)
    {
        var b = await Ok(_c.GetAsync("/api/bootstrap"));
        return b.GetProperty("products").EnumerateArray().First(p => p.GetProperty("id").GetInt32() == id);
    }

    private async Task<decimal> Stock(int id) => (await Product(id)).GetProperty("stock").GetDecimal();

    private async Task<JsonElement[]> Cash() => (await Ok(_c.GetAsync("/api/bootstrap"))).GetProperty("cash").EnumerateArray().ToArray();

    private async Task<int> Sell(int productId, decimal qty, string pay = "Nakit", string? customer = null)
    {
        var r = await Ok(Post("/api/sales", new { paymentType = pay, customer, lines = new[] { new { productId, quantity = qty } } }));
        return r.GetProperty("auditId").GetInt32();
    }

    /* ---------------- Kimlik doğrulama ve yetki ---------------- */

    [Fact]
    public async Task Kurulum_giris_ve_yetkiler()
    {
        var anon = await Fails(_c.GetAsync("/api/bootstrap"));
        Assert.Equal(HttpStatusCode.Unauthorized, anon.Code);

        await Setup();
        var again = await Fails(Post("/api/auth/setup", new { shopName = "X", userName = "baska", password = "12345678" }));
        Assert.Equal(HttpStatusCode.Conflict, again.Code);

        await Ok(Post("/api/users", new { userName = "calisan1", displayName = "Ayşe", role = "Calisan", password = "calisan-sifre" }));
        await Ok(Post("/api/auth/logout"));

        var bad = await Fails(Post("/api/auth/login", new { userName = "calisan1", password = "yanlis-sifre" }));
        Assert.Equal(HttpStatusCode.Unauthorized, bad.Code);

        await Ok(Post("/api/auth/login", new { userName = "calisan1", password = "calisan-sifre" }));
        var boot = await Ok(_c.GetAsync("/api/bootstrap"));
        Assert.Equal(JsonValueKind.Null, boot.GetProperty("cash").ValueKind); // çalışan finansı görmez
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(_c.GetAsync("/api/history"))).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(_c.GetAsync("/api/reports"))).Code);
        Assert.Equal(HttpStatusCode.Forbidden, (await Fails(Post("/api/cash", new { type = "Gelir", amount = 5 }))).Code);
    }

    [Fact]
    public async Task Ozel_baslik_olmadan_degisiklik_reddedilir()
    {
        await Setup();
        var raw = _app.CreateClient(); // X-Requested-With yok
        var r = await raw.PostAsJsonAsync("/api/auth/login", new { userName = "sahip", password = "gizli-sifre-1" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Devre_disi_birakilan_kullanicinin_oturumu_kapanir()
    {
        await Setup();
        var u = await Ok(Post("/api/users", new { userName = "gecici", displayName = "Geçici", role = "Calisan", password = "gecici-sifre" }));
        var uid = u.GetProperty("data").GetProperty("id").GetInt32();

        var staff = _app.Client();
        await Ok(staff.PostAsJsonAsync("/api/auth/login", new { userName = "gecici", password = "gecici-sifre" }));
        await Ok(staff.GetAsync("/api/bootstrap"));

        await Ok(Put($"/api/users/{uid}", new { displayName = "Geçici", role = "Calisan", isActive = false }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/bootstrap")).StatusCode);
    }

    [Fact]
    public async Task Son_sahip_devre_disi_birakilamaz()
    {
        await Setup();
        var me = (await Ok(_c.GetAsync("/api/bootstrap"))).GetProperty("me").GetProperty("id").GetInt32();
        var (_, err) = await Fails(Put($"/api/users/{me}", new { displayName = "Ahmet", role = "Calisan", isActive = true }));
        Assert.Contains("en az bir aktif sahip", err, StringComparison.OrdinalIgnoreCase);
    }

    /* ---------------- Satış ve stok tutarlılığı ---------------- */

    [Fact]
    public async Task Satis_stogu_dusurur_gelir_ve_hareket_olusturur()
    {
        await Setup();
        var pid = await NewProduct(stock: 10, price: 100);
        await Ok(Post("/api/sales", new { paymentType = "Kart", discount = 20, lines = new[] { new { productId = pid, quantity = 3m } } }));

        Assert.Equal(7, await Stock(pid));
        var cash = (await Cash()).Single(c => c.GetProperty("category").GetString() == "Satış");
        Assert.Equal(280, cash.GetProperty("amount").GetDecimal()); // 3 × 100 − 20 indirim
        Assert.True(cash.GetProperty("linked").GetBoolean());

        var mv = await Ok(_c.GetAsync($"/api/products/{pid}/movements"));
        var types = mv.GetProperty("movements").EnumerateArray().Select(m => m.GetProperty("type").GetString()).ToArray();
        Assert.Equal(["Satis", "Acilis"], types);

        var (_, err) = await Fails(Post("/api/sales", new { paymentType = "Nakit", lines = new[] { new { productId = pid, quantity = 8m } } }));
        Assert.Contains("Yetersiz stok", err);
    }

    [Fact]
    public async Task Satisa_bagli_gelir_kaydi_elle_silinemez()
    {
        await Setup();
        var pid = await NewProduct();
        await Sell(pid, 1);
        var id = (await Cash()).Single().GetProperty("id").GetInt32();
        var (_, err) = await Fails(_c.DeleteAsync($"/api/cash/{id}"));
        Assert.Contains("satıştan otomatik oluştu", err);
    }

    [Fact]
    public async Task Kapasite_ve_eksi_stok_korunur()
    {
        await Setup();
        var pid = await NewProduct(stock: 10, capacity: 20);
        Assert.Contains("Kapasite aşılıyor", (await Fails(Post($"/api/products/{pid}/stock-in", new { quantity = 11m }))).Error);
        Assert.Contains("eksiye", (await Fails(Post($"/api/products/{pid}/adjust", new { type = "Fire", quantity = 11m }))).Error);
        await Ok(Post($"/api/products/{pid}/adjust", new { type = "Sayim", quantity = 8m, note = "Ay sonu sayımı" }));
        Assert.Equal(8, await Stock(pid));
    }

    /* ---------------- Geri alma ---------------- */

    [Fact]
    public async Task Satis_geri_alinir_ve_yeniden_uygulanir()
    {
        await Setup();
        var pid = await NewProduct(stock: 10);
        var audit = await Sell(pid, 4);
        Assert.Equal(6, await Stock(pid));

        var undo = await Ok(Post($"/api/history/{audit}/undo"));
        Assert.Equal(10, await Stock(pid));
        Assert.Empty(await Cash());
        Assert.Empty((await Ok(_c.GetAsync("/api/sales"))).EnumerateArray());

        // Geri almayı geri al = yeniden uygula
        await Ok(Post($"/api/history/{undo.GetProperty("auditId").GetInt32()}/undo"));
        Assert.Equal(6, await Stock(pid));
        Assert.Single(await Cash());

        // Asıl kayıt tekrar "aktif" ve yeniden geri alınabilir
        await Ok(Post($"/api/history/{audit}/undo"));
        Assert.Equal(10, await Stock(pid));
    }

    [Fact]
    public async Task Sonradan_degisen_kayit_icin_geri_alma_reddedilir_ve_engel_listelenir()
    {
        await Setup();
        var pid = await NewProduct(stock: 10);
        var first = await Sell(pid, 1);
        var second = await Sell(pid, 2);

        var r = await Post($"/api/history/{first}/undo");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var body = await Json(r);
        Assert.Contains($"#{second}", body.GetProperty("details")[0].GetString());
        Assert.Equal(7, await Stock(pid)); // hiçbir şey değişmedi

        await Ok(Post($"/api/history/{second}/undo"));
        await Ok(Post($"/api/history/{first}/undo"));
        Assert.Equal(10, await Stock(pid));
    }

    [Fact]
    public async Task Silinen_urun_ve_duzenlenen_kayit_geri_alinir()
    {
        await Setup();
        var pid = await NewProduct(name: "Bal");
        var del = await Ok(_c.DeleteAsync($"/api/products/{pid}"));
        Assert.DoesNotContain((await Ok(_c.GetAsync("/api/bootstrap"))).GetProperty("products").EnumerateArray(), p => p.GetProperty("id").GetInt32() == pid);
        await Ok(Post($"/api/history/{del.GetProperty("auditId").GetInt32()}/undo"));
        Assert.Equal("Bal", (await Product(pid)).GetProperty("name").GetString());

        var c = await Ok(Post("/api/cash", new { type = "Gider", amount = 500m, category = "Kira" }));
        var cid = c.GetProperty("data").GetProperty("id").GetInt32();
        var edit = await Ok(Put($"/api/cash/{cid}", new { type = "Gider", amount = 750m, category = "Kira" }));
        await Ok(Post($"/api/history/{edit.GetProperty("auditId").GetInt32()}/undo"));
        Assert.Equal(500, (await Cash()).Single().GetProperty("amount").GetDecimal());

        var detail = await Ok(_c.GetAsync($"/api/history/{edit.GetProperty("auditId").GetInt32()}"));
        var field = detail.GetProperty("changes")[0].GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("field").GetString() == "Amount");
        Assert.Equal("500", field.GetProperty("before").GetString());
        Assert.Equal("750", field.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Satis_iptali_stogu_geri_getirir_ve_geri_alinabilir()
    {
        await Setup();
        var pid = await NewProduct(stock: 10);
        await Sell(pid, 3);
        var saleId = (await Ok(_c.GetAsync("/api/sales")))[0].GetProperty("id").GetInt32();

        var cancel = await Ok(Post($"/api/sales/{saleId}/cancel", new { reason = "Müşteri iade etti" }));
        Assert.Equal(10, await Stock(pid));
        Assert.Empty(await Cash()); // aynı gün iptal: gelir kaydı silinir
        Assert.Contains("zaten iptal", (await Fails(Post($"/api/sales/{saleId}/cancel", new { }))).Error);

        await Ok(Post($"/api/history/{cancel.GetProperty("auditId").GetInt32()}/undo"));
        Assert.Equal(7, await Stock(pid));
        Assert.False((await Ok(_c.GetAsync($"/api/sales/{saleId}"))).GetProperty("isCancelled").GetBoolean());
    }

    /* ---------------- Borç / alacak ---------------- */

    [Fact]
    public async Task Veresiye_satis_alacak_olusturur_tahsilat_ve_iptal()
    {
        await Setup();
        var pid = await NewProduct(stock: 10, price: 50);
        Assert.Contains("müşteri", (await Fails(Post("/api/sales", new { paymentType = "Veresiye", lines = new[] { new { productId = pid, quantity = 2m } } }))).Error);
        await Sell(pid, 2, "Veresiye", "Mehmet Bey");

        var debt = (await Ok(_c.GetAsync("/api/bootstrap"))).GetProperty("debts")[0];
        var did = debt.GetProperty("id").GetInt32();
        Assert.Equal(100, debt.GetProperty("remaining").GetDecimal());
        Assert.Empty(await Cash()); // nakit esası: tahsil edilene kadar gelir yok

        Assert.Contains("büyük olamaz", (await Fails(Post($"/api/debts/{did}/payments", new { amount = 150m }))).Error);
        await Ok(Post($"/api/debts/{did}/payments", new { amount = 40m }));
        debt = (await Ok(_c.GetAsync("/api/bootstrap"))).GetProperty("debts")[0];
        Assert.Equal(60, debt.GetProperty("remaining").GetDecimal());
        Assert.Equal("Tahsilat", (await Cash()).Single().GetProperty("category").GetString());

        var saleId = (await Ok(_c.GetAsync("/api/sales")))[0].GetProperty("id").GetInt32();
        Assert.Contains("tahsilat", (await Fails(Post($"/api/sales/{saleId}/cancel", new { }))).Error);

        var payId = debt.GetProperty("payments")[0].GetProperty("id").GetInt32();
        await Ok(_c.DeleteAsync($"/api/debts/{did}/payments/{payId}"));
        Assert.Empty(await Cash());
        await Ok(Post($"/api/sales/{saleId}/cancel", new { }));
        Assert.Equal(10, await Stock(pid));
    }

    [Fact]
    public async Task Vadeli_stok_girisi_tedarikciye_borc_yazar()
    {
        await Setup();
        var s = await Ok(Post("/api/suppliers", new { name = "Toptancı" }));
        var sid = s.GetProperty("data").GetProperty("id").GetInt32();
        var pid = await NewProduct(stock: 0, capacity: 100, cost: 10);
        await Ok(Post($"/api/products/{pid}/stock-in", new { quantity = 20m, unitCost = 12m, supplierId = sid, credit = true }));
        var debt = (await Ok(_c.GetAsync("/api/bootstrap"))).GetProperty("debts")[0];
        Assert.Equal(240, debt.GetProperty("amount").GetDecimal());
        Assert.Equal("Borc", debt.GetProperty("kind").GetString());
        Assert.Equal(20, await Stock(pid));
        Assert.Equal(12, (await Product(pid)).GetProperty("cost").GetDecimal());
    }

    /* ---------------- Geçmişe dönük kontrol, örnek veri, raporlar ---------------- */

    [Fact]
    public async Task Gecmis_tarihteki_stok_hesaplanir()
    {
        await Setup();
        var pid = await NewProduct(stock: 10);
        await Sell(pid, 3);
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1).ToString("yyyy-MM-dd");
        var mv = await Ok(_c.GetAsync($"/api/products/{pid}/movements?at={yesterday}"));
        Assert.Equal(0, mv.GetProperty("stockAt").GetDecimal()); // ürün bugün açıldı
        var today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        mv = await Ok(_c.GetAsync($"/api/products/{pid}/movements?at={today}"));
        Assert.Equal(7, mv.GetProperty("stockAt").GetDecimal());
    }

    [Fact]
    public async Task Ornek_veride_stoklar_hareketlerle_tutarli()
    {
        await Setup(demo: true);
        var boot = await Ok(_c.GetAsync("/api/bootstrap"));
        var products = boot.GetProperty("products").EnumerateArray().ToArray();
        Assert.Equal(8, products.Length);
        foreach (var p in products)
        {
            var mv = await Ok(_c.GetAsync($"/api/products/{p.GetProperty("id").GetInt32()}/movements"));
            var sum = mv.GetProperty("movements").EnumerateArray().Sum(m => m.GetProperty("quantity").GetDecimal());
            Assert.Equal(p.GetProperty("stock").GetDecimal(), sum);
            Assert.True(p.GetProperty("stock").GetDecimal() >= 0);
            Assert.True(p.GetProperty("stock").GetDecimal() <= p.GetProperty("capacity").GetDecimal());
        }
        Assert.NotEmpty((await Ok(_c.GetAsync("/api/sales"))).EnumerateArray());
        Assert.NotEmpty((await Ok(_c.GetAsync("/api/notifications"))).EnumerateArray());
    }

    [Fact]
    public async Task Rapor_ve_excel_uretilir()
    {
        await Setup(demo: true);
        var rep = await Ok(_c.GetAsync("/api/reports"));
        Assert.True(rep.GetProperty("sales").GetProperty("count").GetInt32() > 0);
        Assert.True(rep.GetProperty("topProducts").GetArrayLength() > 0);
        Assert.True(rep.GetProperty("vat").GetProperty("output").GetDecimal() > 0);

        var x = await _c.GetAsync("/api/reports/excel");
        Assert.True(x.IsSuccessStatusCode);
        var bytes = await x.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]); // .xlsx = zip
        Assert.Equal((byte)'K', bytes[1]);

        var backup = await _c.GetAsync("/api/admin/backup");
        Assert.True(backup.IsSuccessStatusCode);
        Assert.StartsWith("SQLite format 3", System.Text.Encoding.ASCII.GetString(await backup.Content.ReadAsByteArrayAsync(), 0, 15));
    }

    [Fact]
    public async Task Calisan_gecmis_tarihe_satis_giremez()
    {
        await Setup();
        var pid = await NewProduct();
        await Ok(Post("/api/users", new { userName = "kasa", displayName = "Kasa", role = "Calisan", password = "kasa-sifre-1" }));
        var staff = _app.Client();
        await Ok(staff.PostAsJsonAsync("/api/auth/login", new { userName = "kasa", password = "kasa-sifre-1" }));
        var r = await staff.PostAsJsonAsync("/api/sales", new { date = "2020-01-01", paymentType = "Nakit", lines = new[] { new { productId = pid, quantity = 1m } } });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var boot = await Ok(staff.GetAsync("/api/bootstrap"));
        Assert.Equal(JsonValueKind.Null, boot.GetProperty("products")[0].GetProperty("cost").ValueKind);
    }
}
