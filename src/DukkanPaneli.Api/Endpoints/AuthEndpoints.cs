using System.Security.Claims;
using System.Text.RegularExpressions;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Endpoints;

public sealed record SetupReq(string? ShopName, string? DisplayName, string? UserName, string? Password, bool LoadDemo);
public sealed record LoginReq(string? UserName, string? Password);
public sealed record ChangePasswordReq(string? Current, string? Next);
public sealed record CreateUserReq(string? UserName, string? DisplayName, Role Role, string? Password);
public sealed record UpdateUserReq(string? DisplayName, Role Role, bool IsActive, string? NewPassword);

public static partial class AuthEndpoints
{
    [GeneratedRegex("^[a-z0-9._-]{3,32}$")]
    private static partial Regex UserNameRx();

    private static object Me(User u) => new { u.Id, u.UserName, u.DisplayName, u.Role };

    private static string? CheckUserName(string? name) =>
        UserNameRx().IsMatch(name ?? "") ? null : "Kullanıcı adı 3-32 karakter olmalı; sadece küçük harf, rakam, nokta, tire ve alt çizgi içerebilir.";

    private static string? CheckPassword(string? pw) =>
        (pw ?? "").Length >= 8 ? null : "Şifre en az 8 karakter olmalı.";

    public static async Task SignInAsync(HttpContext ctx, User u)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, u.Id.ToString()),
            new(ClaimTypes.Name, u.UserName),
            new("display", u.DisplayName),
            new(ClaimTypes.Role, u.Role.ToString()),
            new("stamp", u.SecurityStamp),
        };
        var id = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id), new AuthenticationProperties { IsPersistent = true });
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/status", async (HttpContext ctx, AppDb db) =>
        {
            var needsSetup = !await db.Users.AnyAsync();
            var shop = await db.Settings.Where(s => s.Key == "shopName").Select(s => s.Value).FirstOrDefaultAsync() ?? "Dükkan Paneli";
            object? me = null;
            if (ctx.User.Identity?.IsAuthenticated == true && await db.Users.FindAsync(ApiKit.ActorOf(ctx.User).Id) is { } u) me = Me(u);
            return Results.Ok(new { needsSetup, shopName = shop, user = me });
        });

        // İlk kurulum: sadece hiç kullanıcı yokken çalışır ve sahip hesabını oluşturur.
        auth.MapPost("/setup", async (SetupReq req, HttpContext ctx, AppDb db, IPasswordHasher<User> hasher) =>
        {
            if (await db.Users.AnyAsync()) return ApiKit.Fail("Kurulum zaten yapılmış.", 409);
            var userName = (req.UserName ?? "").Trim().ToLowerInvariant();
            if (CheckUserName(userName) is { } e1) return ApiKit.Fail(e1);
            if (CheckPassword(req.Password) is { } e2) return ApiKit.Fail(e2);
            var shop = ApiKit.Clean(req.ShopName, 60);
            if (shop.Length == 0) return ApiKit.Fail("Dükkan adı gerekli.");

            var u = new User { UserName = userName, DisplayName = ApiKit.Clean(req.DisplayName, 60) is { Length: > 0 } d ? d : userName, Role = Role.Sahip, CreatedAt = DateTime.UtcNow };
            u.PasswordHash = hasher.HashPassword(u, req.Password!);
            db.Users.Add(u);
            db.Settings.AddRange(new Setting { Key = "shopName", Value = shop }, new Setting { Key = "defaultVatRate", Value = "20" });
            await db.SaveChangesAsync();
            if (req.LoadDemo) await DemoData.LoadAsync(db, new Actor(u.Id, u.DisplayName, u.Role));
            await SignInAsync(ctx, u);
            return Results.Ok(new { user = Me(u) });
        });

        auth.MapPost("/login", async (LoginReq req, HttpContext ctx, AppDb db, IPasswordHasher<User> hasher) =>
        {
            var userName = (req.UserName ?? "").Trim().ToLowerInvariant();
            var u = await db.Users.FirstOrDefaultAsync(x => x.UserName == userName);
            var result = u is null ? PasswordVerificationResult.Failed : hasher.VerifyHashedPassword(u, u.PasswordHash, req.Password ?? "");
            if (u is null || result == PasswordVerificationResult.Failed) return ApiKit.Fail("Kullanıcı adı veya şifre hatalı.", 401);
            if (!u.IsActive) return ApiKit.Fail("Bu hesap devre dışı bırakılmış. Dükkan sahibiyle görüşün.", 403);
            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                u.PasswordHash = hasher.HashPassword(u, req.Password!);
                await db.SaveChangesAsync();
            }
            await SignInAsync(ctx, u);
            return Results.Ok(new { user = Me(u) });
        }).RequireRateLimiting("login");

        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        auth.MapPost("/change-password", async (ChangePasswordReq req, HttpContext ctx, AppDb db, IPasswordHasher<User> hasher) =>
        {
            var u = await db.Users.FindAsync(ApiKit.ActorOf(ctx.User).Id);
            if (u is null) return Results.Unauthorized();
            if (hasher.VerifyHashedPassword(u, u.PasswordHash, req.Current ?? "") == PasswordVerificationResult.Failed) return ApiKit.Fail("Mevcut şifre hatalı.");
            if (CheckPassword(req.Next) is { } e) return ApiKit.Fail(e);
            u.PasswordHash = hasher.HashPassword(u, req.Next!);
            u.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "user.password", () => $"Şifre değiştirildi: {u.UserName}", undoable: false);
            await SignInAsync(ctx, u);
            return Results.Ok();
        }).RequireAuthorization();

        // ---- Kullanıcı yönetimi (yalnızca sahip) ----
        var users = app.MapGroup("/api/users").RequireAuthorization("Sahip");

        users.MapGet("/", async (AppDb db) =>
            await db.Users.OrderBy(u => u.Id).Select(u => new { u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.CreatedAt }).ToListAsync());

        users.MapPost("/", async (CreateUserReq req, HttpContext ctx, AppDb db, IPasswordHasher<User> hasher) =>
        {
            var userName = (req.UserName ?? "").Trim().ToLowerInvariant();
            if (CheckUserName(userName) is { } e1) return ApiKit.Fail(e1);
            if (CheckPassword(req.Password) is { } e2) return ApiKit.Fail(e2);
            if (await db.Users.AnyAsync(x => x.UserName == userName)) return ApiKit.Fail("Bu kullanıcı adı zaten kullanılıyor.");
            var u = new User { UserName = userName, DisplayName = ApiKit.Clean(req.DisplayName, 60) is { Length: > 0 } d ? d : userName, Role = req.Role, CreatedAt = DateTime.UtcNow };
            u.PasswordHash = hasher.HashPassword(u, req.Password!);
            db.Users.Add(u);
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "user.create", () => $"Kullanıcı eklendi: {u.DisplayName} ({u.UserName}, {RoleTr(u.Role)})", undoable: false);
            return ApiKit.Done(log, new { u.Id });
        });

        users.MapPut("/{id:int}", async (int id, UpdateUserReq req, HttpContext ctx, AppDb db, IPasswordHasher<User> hasher) =>
        {
            var u = await db.Users.FindAsync(id);
            if (u is null) return ApiKit.Fail("Kullanıcı bulunamadı.", 404);
            var losingOwner = u.Role == Role.Sahip && u.IsActive && (req.Role != Role.Sahip || !req.IsActive);
            if (losingOwner && await db.Users.CountAsync(x => x.Role == Role.Sahip && x.IsActive) <= 1)
                return ApiKit.Fail("En az bir aktif sahip hesabı kalmalı.");
            var changes = new List<string>();
            if (u.Role != req.Role) changes.Add($"rol {RoleTr(u.Role)} → {RoleTr(req.Role)}");
            if (u.IsActive != req.IsActive) changes.Add(req.IsActive ? "etkinleştirildi" : "devre dışı bırakıldı");
            var display = ApiKit.Clean(req.DisplayName, 60);
            if (display.Length > 0 && display != u.DisplayName) { changes.Add($"ad {u.DisplayName} → {display}"); u.DisplayName = display; }
            if (!string.IsNullOrEmpty(req.NewPassword))
            {
                if (CheckPassword(req.NewPassword) is { } e) return ApiKit.Fail(e);
                u.PasswordHash = hasher.HashPassword(u, req.NewPassword);
                changes.Add("şifre sıfırlandı");
            }
            if (u.Role != req.Role || u.IsActive != req.IsActive || !string.IsNullOrEmpty(req.NewPassword))
                u.SecurityStamp = Guid.NewGuid().ToString("N"); // açık oturumları kapat
            u.Role = req.Role;
            u.IsActive = req.IsActive;
            var log = await db.SaveAuditedAsync(ApiKit.ActorOf(ctx.User), "user.update",
                () => $"Kullanıcı güncellendi: {u.UserName}" + (changes.Count > 0 ? $" ({string.Join(", ", changes)})" : ""), undoable: false);
            return ApiKit.Done(log);
        });
    }

    public static string RoleTr(Role r) => r == Role.Sahip ? "Sahip" : "Çalışan";
}
