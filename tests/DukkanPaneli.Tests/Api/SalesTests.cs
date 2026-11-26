using System.Net;
using System.Text.Json;
using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

public class SalesTests : ApiTestBase
{
    [Fact]
    public async Task Satis_stogu_dusurur_gelir_ve_hareket_olusturur()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10, price: 100);
        await Ok(Post("/api/sales", new { paymentType = "Kart", discount = 20, lines = new[] { new { productId = pid, quantity = 3m } } }));

        Assert.Equal(7, await Stock(pid));
        var income = (await CashEntries()).Single(c => c.GetProperty("category").GetString() == "Satış");
        Assert.Equal(280, income.GetProperty("amount").GetDecimal()); // 3 × 100 − 20 indirim
        Assert.True(income.GetProperty("linked").GetBoolean());

        var mv = await Ok(Get($"/api/products/{pid}/movements"));
        var types = mv.GetProperty("movements").EnumerateArray().Select(m => m.GetProperty("type").GetString()!).ToArray();
        Assert.Equal(new[] { "Satis", "Acilis" }, types);

        var (_, err) = await Fails(Post("/api/sales", new { paymentType = "Nakit", lines = new[] { new { productId = pid, quantity = 8m } } }));
        Assert.Contains("Yetersiz stok", err);
    }

    [Fact]
    public async Task Ayni_urun_iki_kez_eklenirse_tek_kalemde_birlesir()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10, price: 10);
        await Ok(Post("/api/sales", new { paymentType = "Nakit", lines = new[] { new { productId = pid, quantity = 2m }, new { productId = pid, quantity = 3m } } }));

        var sale = (await Ok(Get("/api/sales")))[0];
        Assert.Single(sale.GetProperty("lines").EnumerateArray());
        Assert.Equal(5, sale.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(50, sale.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Satisa_bagli_gelir_kaydi_elle_silinemez()
    {
        await SetupOwner();
        var pid = await NewProduct();
        await Sell(pid, 1);
        var id = (await CashEntries()).Single().GetProperty("id").GetInt32();
        var (_, err) = await Fails(Delete($"/api/cash/{id}"));
        Assert.Contains("satıştan otomatik oluştu", err);
    }

    [Fact]
    public async Task Satis_iptali_stogu_geri_getirir_ve_geri_alinabilir()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10);
        await Sell(pid, 3);
        var saleId = (await Ok(Get("/api/sales")))[0].GetProperty("id").GetInt32();

        var cancel = await Ok(Post($"/api/sales/{saleId}/cancel", new { reason = "Müşteri iade etti" }));
        Assert.Equal(10, await Stock(pid));
        Assert.Empty(await CashEntries()); // aynı gün iptal: gelir kaydı silinir
        Assert.Contains("zaten iptal", (await Fails(Post($"/api/sales/{saleId}/cancel"))).Error);

        await Ok(Post($"/api/history/{AuditId(cancel)}/undo"));
        Assert.Equal(7, await Stock(pid));
        Assert.False((await Ok(Get($"/api/sales/{saleId}"))).GetProperty("isCancelled").GetBoolean());
    }

    [Fact]
    public async Task Calisan_gecmis_tarihe_satis_giremez_maliyeti_goremez_ve_iptal_edemez()
    {
        await SetupOwner();
        var pid = await NewProduct();
        var staff = await StaffClient();

        var backdated = await staff.PostAsync("/api/sales", JsonBody(new { date = "2020-01-01", paymentType = "Nakit", lines = new[] { new { productId = pid, quantity = 1m } } }));
        Assert.Equal(HttpStatusCode.BadRequest, backdated.StatusCode);

        var boot = await Ok(staff.GetAsync("/api/bootstrap"));
        Assert.Equal(JsonValueKind.Null, boot.GetProperty("products")[0].GetProperty("cost").ValueKind);

        await Sell(pid, 1);
        var saleId = (await Ok(Get("/api/sales")))[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync($"/api/sales/{saleId}/cancel", JsonBody(new { }))).StatusCode);
    }

    private static StringContent JsonBody(object o) =>
        new(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");
}
