using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DukkanPaneli.Tests.Support;

/// <summary>
/// Entegrasyon testleri için ortak yardımcılar. xUnit her test metodu için sınıfın yeni bir örneğini oluşturur,
/// yani her test temiz bir veritabanıyla başlar; Dispose sonunda uygulamayı kapatır.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    protected readonly TestApp App = new();
    protected readonly HttpClient Http;

    protected ApiTestBase() => Http = App.Client();

    public void Dispose()
    {
        App.Dispose();
        GC.SuppressFinalize(this);
    }

    /* ---------------- istek yardımcıları ---------------- */

    protected Task<HttpResponseMessage> Get(string url) => Http.GetAsync(url);
    protected Task<HttpResponseMessage> Post(string url, object? body = null) => Http.PostAsJsonAsync(url, body ?? new { });
    protected Task<HttpResponseMessage> Put(string url, object body) => Http.PutAsJsonAsync(url, body);
    protected Task<HttpResponseMessage> Delete(string url) => Http.DeleteAsync(url);

    protected static async Task<JsonElement> ReadJson(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>İsteğin başarılı olmasını bekler ve JSON gövdesini döndürür.</summary>
    protected static async Task<JsonElement> Ok(Task<HttpResponseMessage> call)
    {
        var r = await call;
        var body = await ReadJson(r);
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode}: {body}");
        return body;
    }

    /// <summary>İsteğin başarısız olmasını bekler; durum kodunu ve hata mesajını döndürür.</summary>
    protected static async Task<(HttpStatusCode Code, string Error)> Fails(Task<HttpResponseMessage> call)
    {
        var r = await call;
        Assert.False(r.IsSuccessStatusCode, "İstek başarısız olmalıydı.");
        var body = await ReadJson(r);
        var error = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("error", out var e) ? e.GetString()! : "";
        return (r.StatusCode, error);
    }

    /* ---------------- senaryo yardımcıları ---------------- */

    protected Task SetupOwner(bool demo = false) =>
        Ok(Post("/api/auth/setup", new { shopName = "Test Dükkan", displayName = "Ahmet", userName = "sahip", password = "gizli-sifre-1", loadDemo = demo }));

    /// <summary>Çalışan hesabı açar ve o hesapla giriş yapmış ayrı bir istemci döndürür.</summary>
    protected async Task<HttpClient> StaffClient(string userName = "kasa", string password = "kasa-sifre-1")
    {
        await Ok(Post("/api/users", new { userName, displayName = "Kasa", role = "Calisan", password }));
        var staff = App.Client();
        await Ok(staff.PostAsJsonAsync("/api/auth/login", new { userName, password }));
        return staff;
    }

    protected async Task<int> NewProduct(string name = "Çay", decimal stock = 10, decimal capacity = 50, decimal price = 100, decimal cost = 60)
    {
        var r = await Ok(Post("/api/products", new { name, category = "İçecek", unit = "adet", stock, capacity, minStock = 2, cost, price, vatRate = 20 }));
        return r.GetProperty("data").GetProperty("id").GetInt32();
    }

    /// <summary>Tek kalemli satış yapar ve geçmiş kaydının (audit) numarasını döndürür.</summary>
    protected async Task<int> Sell(int productId, decimal qty, string pay = "Nakit", string? customer = null)
    {
        var r = await Ok(Post("/api/sales", new { paymentType = pay, customer, lines = new[] { new { productId, quantity = qty } } }));
        return r.GetProperty("auditId").GetInt32();
    }

    protected static int AuditId(JsonElement mutationResponse) => mutationResponse.GetProperty("auditId").GetInt32();

    protected Task<JsonElement> Bootstrap() => Ok(Get("/api/bootstrap"));

    protected async Task<JsonElement> Product(int id) =>
        (await Bootstrap()).GetProperty("products").EnumerateArray().First(p => p.GetProperty("id").GetInt32() == id);

    protected async Task<decimal> Stock(int productId) => (await Product(productId)).GetProperty("stock").GetDecimal();

    protected async Task<JsonElement[]> CashEntries() => (await Bootstrap()).GetProperty("cash").EnumerateArray().ToArray();

    protected async Task<JsonElement> FirstDebt() => (await Bootstrap()).GetProperty("debts")[0];
}
