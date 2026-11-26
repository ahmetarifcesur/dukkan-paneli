using System.Net;
using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

/// <summary>İşlem geçmişi ve geri alma (AppDb.Audit.cs).</summary>
public class UndoTests : ApiTestBase
{
    [Fact]
    public async Task Satis_geri_alinir_ve_yeniden_uygulanir()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10);
        var sale = await Sell(pid, 4);
        Assert.Equal(6, await Stock(pid));

        var undo = await Ok(Post($"/api/history/{sale}/undo"));
        Assert.Equal(10, await Stock(pid));
        Assert.Empty(await CashEntries());
        Assert.Empty((await Ok(Get("/api/sales"))).EnumerateArray());

        // Geri almayı geri almak = yeniden uygulamak
        await Ok(Post($"/api/history/{AuditId(undo)}/undo"));
        Assert.Equal(6, await Stock(pid));
        Assert.Single(await CashEntries());

        // Asıl işlem yeniden "etkin" ve tekrar geri alınabilir
        await Ok(Post($"/api/history/{sale}/undo"));
        Assert.Equal(10, await Stock(pid));
    }

    [Fact]
    public async Task Sonradan_degisen_kayit_icin_geri_alma_reddedilir_ve_engel_listelenir()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10);
        var first = await Sell(pid, 1);
        var second = await Sell(pid, 2);

        var r = await Post($"/api/history/{first}/undo");
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        var body = await ReadJson(r);
        Assert.Contains($"#{second}", body.GetProperty("details")[0].GetString());
        Assert.Equal(7, await Stock(pid)); // hiçbir şey değişmedi

        await Ok(Post($"/api/history/{second}/undo"));
        await Ok(Post($"/api/history/{first}/undo"));
        Assert.Equal(10, await Stock(pid));
    }

    [Fact]
    public async Task Silinen_urun_ve_duzenlenen_kayit_geri_alinir()
    {
        await SetupOwner();
        var pid = await NewProduct(name: "Bal");
        var del = await Ok(Delete($"/api/products/{pid}"));
        Assert.DoesNotContain((await Bootstrap()).GetProperty("products").EnumerateArray(), p => p.GetProperty("id").GetInt32() == pid);
        await Ok(Post($"/api/history/{AuditId(del)}/undo"));
        Assert.Equal("Bal", (await Product(pid)).GetProperty("name").GetString());

        var c = await Ok(Post("/api/cash", new { type = "Gider", amount = 500m, category = "Kira" }));
        var cid = c.GetProperty("data").GetProperty("id").GetInt32();
        var edit = await Ok(Put($"/api/cash/{cid}", new { type = "Gider", amount = 750m, category = "Kira" }));
        await Ok(Post($"/api/history/{AuditId(edit)}/undo"));
        Assert.Equal(500, (await CashEntries()).Single().GetProperty("amount").GetDecimal());

        // Ayrıntı ekranı: hangi alan neyden neye değişti
        var detail = await Ok(Get($"/api/history/{AuditId(edit)}"));
        var field = detail.GetProperty("changes")[0].GetProperty("fields").EnumerateArray()
            .Single(f => f.GetProperty("field").GetString() == "Amount");
        Assert.Equal("500", field.GetProperty("before").GetString());
        Assert.Equal("750", field.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Ayni_islem_iki_kez_geri_alinamaz()
    {
        await SetupOwner();
        var pid = await NewProduct();
        var sale = await Sell(pid, 1);
        await Ok(Post($"/api/history/{sale}/undo"));
        Assert.Contains("zaten geri alınmış", (await Fails(Post($"/api/history/{sale}/undo"))).Error);
    }
}
