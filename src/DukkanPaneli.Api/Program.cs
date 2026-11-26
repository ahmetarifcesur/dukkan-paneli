// Uygulamanın giriş noktası. Ayrıntılar Infrastructure/ klasöründeki dosyalarda;
// burada sadece sırayı görürsünüz: servisleri kaydet → uygulamayı kur → ara katmanlar → uç noktalar.

using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDukkanDatabase(builder.Configuration, builder.Environment)
    .AddDukkanAuth(builder.Environment)
    .AddDukkanServices();

var app = builder.Build();

// Veritabanını en son şemaya getir (ilk açılışta tabloları oluşturur, sonra yeni migration'ları uygular).
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();

// Ara katmanlar (middleware) yazıldığı sırayla çalışır.
app.UseForwardedHeaders(); // ters vekil arkasında gerçek IP ve https bilgisi (bkz. AuthSetup)
if (!app.Environment.IsDevelopment())
    app.UseHsts(); // HTTPS ile açıldıysa tarayıcı bundan sonra siteye hep HTTPS ile gelsin
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { error = "Sunucuda beklenmeyen bir hata oluştu." });
}));
app.UseDukkanSecurity();
app.UseDefaultFiles(); // "/" → wwwroot/index.html
app.UseStaticFiles(new StaticFileOptions
{
    // Güncellemeler hemen görünsün: tarayıcı her seferinde ETag ile kontrol etsin.
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapDukkanEndpoints();

app.Run();

/// <summary>Testlerin (WebApplicationFactory) uygulamaya erişebilmesi için.</summary>
public partial class Program;
