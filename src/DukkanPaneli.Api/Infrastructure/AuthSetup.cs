using System.Security.Claims;
using System.Threading.RateLimiting;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Infrastructure;

/// <summary>Kimlik doğrulama (çerez), yetkilendirme (roller) ve giriş denemesi sınırı.</summary>
public static class AuthSetup
{
    /// <summary>Sadece dükkan sahibinin kullanabileceği uç noktalar için yetki kuralı.</summary>
    public const string OwnerPolicy = "OwnerOnly";

    public const string LoginRateLimit = "login";

    public static IServiceCollection AddDukkanAuth(this IServiceCollection services, IWebHostEnvironment env)
    {
        // Oturum çerezleri bu anahtarlarla şifrelenir. Varsayılan konum sunucu/konteyner yeniden kurulunca
        // kaybolabilir (herkesin oturumu düşer); bu yüzden anahtarları veritabanının yanında saklıyoruz.
        // Windows'ta dosyalar ayrıca DPAPI ile şifrelenir.
        var dp = services.AddDataProtection()
            .SetApplicationName("DukkanPaneli")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(env.ContentRootPath, "App_Data", "keys")));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();

        // Uygulama nginx, Caddy ya da Cloudflare Tunnel gibi bir ters vekilin (reverse proxy) arkasında
        // çalışınca gerçek ziyaretçi IP'si ve HTTPS bilgisi X-Forwarded-* başlıklarıyla gelir. Bu başlıklara
        // sadece aynı makinedeki vekilden (localhost) gelirse güvenilir; yoksa herkes IP'sini taklit edebilirdi.
        services.Configure<ForwardedHeadersOptions>(o =>
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "dukkan.auth";
                o.Cookie.HttpOnly = true;                 // JavaScript okuyamaz (XSS ile çalınamaz)
                o.Cookie.SameSite = SameSiteMode.Strict;  // başka siteden gelen isteklere eklenmez
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;

                // Bu bir API: giriş sayfasına yönlendirmek yerine 401/403 döndür, arayüz kendisi karar versin.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, p => p.RequireRole(nameof(Role.Owner)));

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        // Kaba kuvvet (brute force) koruması: IP başına dakikada 10 giriş denemesi.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(
                new { error = "Çok fazla deneme yapıldı. Bir dakika sonra tekrar deneyin." }, ct);
            o.AddPolicy(LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    /// <summary>
    /// Her istekte çalışır: kullanıcı devre dışı bırakıldıysa ya da şifresi/rolü değiştiyse
    /// (SecurityStamp yenilendiyse) eski çerez artık geçersizdir ve oturum hemen kapanır.
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDb>();
        var idText = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = ctx.Principal?.FindFirstValue(Actor.Claims.SecurityStamp);
        var user = int.TryParse(idText, out var id) ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id) : null;
        if (user is null || !user.IsActive || user.SecurityStamp != stamp)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
