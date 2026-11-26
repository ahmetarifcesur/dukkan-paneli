using DukkanPaneli.Tests.Support;

namespace DukkanPaneli.Tests.Api;

public class ReportAndDemoTests : ApiTestBase
{
    [Fact]
    public async Task Ornek_veride_stoklar_hareketlerle_tutarli()
    {
        await SetupOwner(demo: true);
        var products = (await Bootstrap()).GetProperty("products").EnumerateArray().ToArray();
        Assert.Equal(8, products.Length);

        foreach (var p in products)
        {
            var mv = await Ok(Get($"/api/products/{p.GetProperty("id").GetInt32()}/movements"));
            var sum = mv.GetProperty("movements").EnumerateArray().Sum(m => m.GetProperty("quantity").GetDecimal());
            var stock = p.GetProperty("stock").GetDecimal();
            Assert.Equal(stock, sum); // stok = hareketlerin toplamı
            Assert.InRange(stock, 0, p.GetProperty("capacity").GetDecimal());
        }
        Assert.NotEmpty((await Ok(Get("/api/sales"))).EnumerateArray());
        Assert.NotEmpty((await Ok(Get("/api/notifications"))).EnumerateArray());
    }

    [Fact]
    public async Task Rapor_excel_ve_yedek_uretilir()
    {
        await SetupOwner(demo: true);
        var rep = await Ok(Get("/api/reports"));
        Assert.True(rep.GetProperty("sales").GetProperty("count").GetInt32() > 0);
        Assert.True(rep.GetProperty("topProducts").GetArrayLength() > 0);
        Assert.True(rep.GetProperty("vat").GetProperty("output").GetDecimal() > 0);
        Assert.True(rep.GetProperty("totals").TryGetProperty("gelir", out _)); // arayüzün beklediği ad

        var excel = await Http.GetAsync("/api/reports/excel");
        Assert.True(excel.IsSuccessStatusCode);
        var bytes = await excel.Content.ReadAsByteArrayAsync();
        Assert.Equal("PK"u8.ToArray(), bytes[..2]); // .xlsx aslında bir zip dosyasıdır

        var backup = await Http.GetAsync("/api/admin/backup");
        Assert.True(backup.IsSuccessStatusCode);
        Assert.StartsWith("SQLite format 3", System.Text.Encoding.ASCII.GetString(await backup.Content.ReadAsByteArrayAsync(), 0, 15));
    }
}
