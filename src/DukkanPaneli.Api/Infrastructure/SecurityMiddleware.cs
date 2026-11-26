namespace DukkanPaneli.Api.Infrastructure;

/// <summary>Güvenlik başlıkları ve CSRF koruması.</summary>
public static class SecurityMiddleware
{
    /// <summary>Arayüzün her değişiklik isteğine eklediği başlık (bkz. wwwroot/js/core.js → api()).</summary>
    public const string CsrfHeader = "X-Requested-With";
    public const string CsrfValue = "dukkan";

    public static IApplicationBuilder UseDukkanSecurity(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h.XContentTypeOptions = "nosniff";       // tarayıcı dosya türünü tahmin etmesin
        h["Referrer-Policy"] = "same-origin";
        h.XFrameOptions = "DENY";                // site başka bir sitenin içine gömülemesin (clickjacking)
        // CSP: yalnızca kendi sunucumuzdan script/stil yüklenebilir; enjekte edilen <script> çalışmaz.
        h.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; " +
                                  "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()"; // kullanmadığımız tarayıcı yetkileri kapalı
        h["Cross-Origin-Opener-Policy"] = "same-origin";

        // CSRF: Başka bir site, kullanıcının tarayıcısından bizim API'ye form gönderebilir (çerez otomatik eklenir).
        // Ama özel bir başlık ekleyemez. Bu yüzden değişiklik yapan her /api isteğinde bu başlığı zorunlu tutuyoruz.
        var isApi = ctx.Request.Path.StartsWithSegments("/api");
        var isReadOnly = HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method);
        if (isApi && !isReadOnly && ctx.Request.Headers[CsrfHeader] != CsrfValue)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { error = "Geçersiz istek." });
            return;
        }
        await next();
    });
}
