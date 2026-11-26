using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Endpoints;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Veritabanı (SQLite, App_Data/dukkan.db) ----
var dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDir);
var connectionString = builder.Configuration.GetConnectionString("Default") ?? $"Data Source={Path.Combine(dataDir, "dukkan.db")}";
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite(connectionString));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.Converters.Add(new CleanDecimalConverter());
});

// ---- Kimlik doğrulama: HttpOnly + SameSite=Strict çerez ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "dukkan.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        // API olduğu için yönlendirme yerine 401/403 döndür.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        // Kullanıcı devre dışı bırakıldıysa, rolü ya da şifresi değiştiyse açık oturumu hemen geçersiz kıl.
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
            var idText = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = ctx.Principal?.FindFirstValue("stamp");
            var user = int.TryParse(idText, out var id) ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id) : null;
            if (user is null || !user.IsActive || user.SecurityStamp != stamp)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorizationBuilder().AddPolicy("Sahip", p => p.RequireRole(nameof(Role.Sahip)));
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// ---- Kaba kuvvet koruması: IP başına dakikada 10 giriş denemesi ----
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(new { error = "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin." }, ct);
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.EnsureCreated();

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { error = "Sunucuda beklenmeyen bir hata oluştu." });
}));

// Güvenlik başlıkları + CSRF koruması (değişiklik isteklerinde özel başlık zorunlu: başka siteden form ile gönderilemez).
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h["Referrer-Policy"] = "same-origin";
    h.XFrameOptions = "DENY";
    h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
        && ctx.Request.Headers["X-Requested-With"] != "dukkan")
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { error = "Geçersiz istek." });
        return;
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Güncellemeler hemen görünsün: tarayıcı her seferinde ETag ile kontrol etsin.
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCoreEndpoints();
app.MapCatalogEndpoints();
app.MapSalesEndpoints();
app.MapFinanceEndpoints();
app.MapReportEndpoints();
app.MapFallback("/api/{**rest}", () => ApiKit.Fail("Bulunamadı.", 404));

app.Run();

public partial class Program;
