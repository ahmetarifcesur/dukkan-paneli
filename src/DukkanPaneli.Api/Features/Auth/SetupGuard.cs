using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DukkanPaneli.Api.Features.Auth;

/// <summary>
/// İlk kurulumu kimin yapabileceğine karar verir.
/// Kurulum, hiç kullanıcı yokken sahip hesabını oluşturur. Sunucu internete açıldığında kurulum henüz
/// yapılmamışsa, siteyi ilk açan yabancı dükkanın sahibi olurdu. Bu yüzden:
/// <list type="bullet">
/// <item><c>Setup:Token</c> ayarlıysa (ör. <c>Setup__Token</c> ortam değişkeni) sadece doğru anahtarı girenler kurabilir.</item>
/// <item>Ayarlı değilse: geliştirme ortamında herkes; yayında (Production) sadece sunucunun kendisi (localhost).</item>
/// </list>
/// </summary>
public sealed class SetupGuard(IConfiguration config, IWebHostEnvironment env)
{
    public const string TokenKey = "Setup:Token";

    private string? Token => config[TokenKey] is { Length: > 0 } t ? t : null;

    public bool TokenRequired => Token is not null;

    public bool IsAllowed(HttpContext ctx, string? providedToken)
    {
        if (Token is { } token)
            // Sabit zamanlı karşılaştırma: yanıt süresinden anahtarın kaç karakterinin doğru olduğu anlaşılamasın.
            return CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(providedToken ?? "")),
                SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        return env.IsDevelopment() || ctx.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);
    }
}
