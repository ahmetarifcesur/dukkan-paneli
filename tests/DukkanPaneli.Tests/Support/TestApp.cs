using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DukkanPaneli.Tests.Support;

/// <summary>
/// Uygulamayı bellekte, gerçek bir HTTP hattıyla çalıştırır. Her örnek kendi geçici SQLite dosyasını kullanır,
/// böylece testler birbirini etkilemez. Program.cs'teki MigrateAsync tabloları bu dosyada oluşturur.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dukkan-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath};Pooling=False");

    /// <summary>Çerezleri saklayan ve CSRF başlığını gönderen istemci (tarayıcı gibi davranır).</summary>
    public HttpClient Client()
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("X-Requested-With", "dukkan");
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_dbPath); } catch (IOException) { /* geçici dosya; silinemezse sorun değil */ }
    }
}
