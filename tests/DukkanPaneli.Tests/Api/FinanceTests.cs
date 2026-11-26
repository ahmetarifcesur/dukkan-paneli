using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

public class FinanceTests : ApiTestBase
{
    [Fact]
    public async Task Veresiye_satis_alacak_olusturur_tahsilat_ve_iptal()
    {
        await SetupOwner();
        var pid = await NewProduct(stock: 10, price: 50);
        Assert.Contains("müşteri", (await Fails(Post("/api/sales", new { paymentType = "Veresiye", lines = new[] { new { productId = pid, quantity = 2m } } }))).Error);
        await Sell(pid, 2, "Veresiye", "Mehmet Bey");

        var debt = await FirstDebt();
        var did = debt.GetProperty("id").GetInt32();
        Assert.Equal(100, debt.GetProperty("remaining").GetDecimal());
        Assert.Empty(await CashEntries()); // nakit esası: tahsil edilene kadar gelir yok

        Assert.Contains("büyük olamaz", (await Fails(Post($"/api/debts/{did}/payments", new { amount = 150m }))).Error);
        await Ok(Post($"/api/debts/{did}/payments", new { amount = 40m }));
        debt = await FirstDebt();
        Assert.Equal(60, debt.GetProperty("remaining").GetDecimal());
        Assert.Equal("Tahsilat", (await CashEntries()).Single().GetProperty("category").GetString());

        // Tahsilatı olan veresiye satış iptal edilemez
        var saleId = (await Ok(Get("/api/sales")))[0].GetProperty("id").GetInt32();
        Assert.Contains("tahsilat", (await Fails(Post($"/api/sales/{saleId}/cancel"))).Error);

        var payId = debt.GetProperty("payments")[0].GetProperty("id").GetInt32();
        await Ok(Delete($"/api/debts/{did}/payments/{payId}"));
        Assert.Empty(await CashEntries());
        await Ok(Post($"/api/sales/{saleId}/cancel"));
        Assert.Equal(10, await Stock(pid));
    }

    [Fact]
    public async Task Gider_kdv_payi_hesaplanir()
    {
        await SetupOwner();
        await Ok(Post("/api/cash", new { type = "Gider", amount = 120m, vatRate = 20m, category = "Elektrik" }));
        var entry = (await CashEntries()).Single();
        Assert.Equal(20, entry.GetProperty("vatAmount").GetDecimal()); // 120 × 20 / 120
        Assert.False(entry.GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task Elle_borc_kismi_odeme_ve_dogrulamalar()
    {
        await SetupOwner();
        Assert.Contains("Ödenmiş kısım", (await Fails(Post("/api/debts", new { kind = "Borc", party = "Banka", amount = 100m, openingPaid = 150m }))).Error);

        var created = await Ok(Post("/api/debts", new { kind = "Borc", party = "Banka", amount = 1000m, openingPaid = 200m }));
        var id = created.GetProperty("data").GetProperty("id").GetInt32();
        await Ok(Post($"/api/debts/{id}/payments", new { amount = 300m }));

        var debt = await FirstDebt();
        Assert.Equal(500, debt.GetProperty("paid").GetDecimal());
        Assert.Equal(500, debt.GetProperty("remaining").GetDecimal());
        Assert.Equal("Gider", (await CashEntries()).Single().GetProperty("type").GetString());
        Assert.Contains("ödemeleri var", (await Fails(Delete($"/api/debts/{id}"))).Error);
    }
}
