using System.Security.Claims;
using System.Text.RegularExpressions;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Auth;

/// <summary>İlk kurulum, giriş/çıkış ve şifre değiştirme.</summary>
public sealed partial class AuthService(AppDb db, IPasswordHasher<User> hasher, DemoDataService demo)
{
    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex UserNamePattern();

    public static string NormalizeUserName(string? name) => (name ?? "").Trim().ToLowerInvariant();

    public static string? ValidateUserName(string userName) => UserNamePattern().IsMatch(userName)
        ? null
        : "Kullanıcı adı 3-32 karakter olmalı; sadece küçük harf, rakam, nokta, tire ve alt çizgi içerebilir.";

    public static string? ValidatePassword(string? password) =>
        (password ?? "").Length >= 8 ? null : "Şifre en az 8 karakter olmalı.";

    public async Task<AuthStatusDto> StatusAsync(ClaimsPrincipal principal)
    {
        var needsSetup = !await db.Users.AnyAsync();
        var settings = await db.ReadSettingsAsync();
        MeDto? me = null;
        if (principal.Identity?.IsAuthenticated == true && await db.Users.FindAsync(Actor.From(principal).Id) is { } u)
            me = MeDto.From(u);
        return new AuthStatusDto(needsSetup, settings.GetValueOrDefault(Setting.ShopName, "Dükkan Paneli"), me);
    }

    /// <summary>İlk kurulum: sadece hiç kullanıcı yokken çalışır ve sahip hesabını oluşturur.</summary>
    public async Task<Result<User>> SetupAsync(SetupRequest req)
    {
        if (await db.Users.AnyAsync()) return Result<User>.Fail("Kurulum zaten yapılmış.", StatusCodes.Status409Conflict);
        var userName = NormalizeUserName(req.UserName);
        if (ValidateUserName(userName) is { } e1) return Result<User>.Fail(e1);
        if (ValidatePassword(req.Password) is { } e2) return Result<User>.Fail(e2);
        var shop = Input.Clean(req.ShopName, 60);
        if (shop.Length == 0) return Result<User>.Fail("Dükkan adı gerekli.");

        var user = new User
        {
            UserName = userName,
            DisplayName = Input.CleanOr(req.DisplayName, 60, userName),
            Role = Role.Owner,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password!);
        db.Users.Add(user);
        db.Settings.AddRange(
            new Setting { Key = Setting.ShopName, Value = shop },
            new Setting { Key = Setting.DefaultVatRate, Value = "20" });
        await db.SaveChangesAsync();

        if (req.LoadDemo) await demo.LoadAsync(new Actor(user.Id, user.DisplayName, user.Role));
        return Result<User>.Ok(user);
    }

    public async Task<Result<User>> LoginAsync(LoginRequest req)
    {
        var userName = NormalizeUserName(req.UserName);
        var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == userName);
        var check = user is null
            ? PasswordVerificationResult.Failed
            : hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "");

        // Kullanıcı adı mı yanlış, şifre mi? Bilerek söylemiyoruz: saldırgana hangi adların var olduğunu göstermemek için.
        if (user is null || check == PasswordVerificationResult.Failed)
            return Result<User>.Fail("Kullanıcı adı veya şifre hatalı.", StatusCodes.Status401Unauthorized);
        if (!user.IsActive)
            return Result<User>.Fail("Bu hesap devre dışı bırakılmış. Dükkan sahibiyle görüşün.", StatusCodes.Status403Forbidden);

        // Hash algoritması güncellendiyse şifreyi yeni algoritmayla yeniden sakla.
        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, req.Password!);
            await db.SaveChangesAsync();
        }
        return Result<User>.Ok(user);
    }

    public async Task<Result<User>> ChangePasswordAsync(Actor actor, ChangePasswordRequest req)
    {
        var user = await db.Users.FindAsync(actor.Id);
        if (user is null) return Result<User>.Fail("Oturum bulunamadı.", StatusCodes.Status401Unauthorized);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.Current ?? "") == PasswordVerificationResult.Failed)
            return Result<User>.Fail("Mevcut şifre hatalı.");
        if (ValidatePassword(req.Next) is { } e) return Result<User>.Fail(e);

        user.PasswordHash = hasher.HashPassword(user, req.Next!);
        user.SecurityStamp = User.NewStamp(); // diğer cihazlardaki oturumlar kapanır
        await db.SaveAuditedAsync(actor, "user.password", () => $"Şifre değiştirildi: {user.UserName}", undoable: false);
        return Result<User>.Ok(user);
    }

    /// <summary>Oturum çerezini yazar. Çerezde kullanıcı kimliği, rolü ve güvenlik damgası bulunur.</summary>
    public static Task SignInAsync(HttpContext ctx, User u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.UserName),
            new(Actor.Claims.DisplayName, u.DisplayName),
            new(ClaimTypes.Role, u.Role.ToString()),
            new(Actor.Claims.SecurityStamp, u.SecurityStamp),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
