using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DukkanPaneli.Api.Features.Auth;

/// <summary>
/// HTTP uç noktaları. Uç noktalar "ince"dir: isteği alır, servisi çağırır, sonucu HTTP yanıtına çevirir.
/// İş kuralları servislerdedir (AuthService, UserService).
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapGet("/status", async (HttpContext ctx, AuthService svc, SetupGuard guard) =>
        {
            var s = await svc.StatusAsync(ctx.User);
            return Results.Ok(s with { SetupTokenRequired = s.NeedsSetup && guard.TokenRequired });
        });

        auth.MapPost("/setup", async (SetupRequest req, HttpContext ctx, AuthService svc, SetupGuard guard) =>
        {
            if (!guard.IsAllowed(ctx, req.SetupToken))
                return HttpResults.Fail(guard.TokenRequired
                    ? "Kurulum anahtarı hatalı."
                    : "Kurulum yalnızca sunucu bilgisayarından (localhost) ya da kurulum anahtarıyla yapılabilir. " +
                      "Anahtarı Setup__Token ortam değişkeniyle tanımlayın.", StatusCodes.Status403Forbidden);

            var r = await svc.SetupAsync(req);
            if (!r.Succeeded) return HttpResults.Fail(r.Error!, r.Status);
            await AuthService.SignInAsync(ctx, r.Value!);
            return Results.Ok(new { user = MeDto.From(r.Value!) });
        }).RequireRateLimiting(AuthSetup.LoginRateLimit);

        auth.MapPost("/login", async (LoginRequest req, HttpContext ctx, AuthService svc) =>
        {
            var r = await svc.LoginAsync(req);
            if (!r.Succeeded) return HttpResults.Fail(r.Error!, r.Status);
            await AuthService.SignInAsync(ctx, r.Value!);
            return Results.Ok(new { user = MeDto.From(r.Value!) });
        }).RequireRateLimiting(AuthSetup.LoginRateLimit);

        auth.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        auth.MapPost("/change-password", async (ChangePasswordRequest req, HttpContext ctx, AuthService svc) =>
        {
            var r = await svc.ChangePasswordAsync(Actor.From(ctx.User), req);
            if (!r.Succeeded) return HttpResults.Fail(r.Error!, r.Status);
            await AuthService.SignInAsync(ctx, r.Value!); // bu cihazın oturumu yeni damgayla devam etsin
            return Results.Ok();
        }).RequireAuthorization().RequireRateLimiting(AuthSetup.LoginRateLimit); // mevcut şifre tahmin edilmesin

        var users = app.MapGroup("/api/users").RequireAuthorization(AuthSetup.OwnerPolicy);

        users.MapGet("/", async (UserService svc) => Results.Ok(await svc.ListAsync()));

        users.MapPost("/", async (CreateUserRequest req, HttpContext ctx, UserService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), req)).ToHttp());

        users.MapPut("/{id:int}", async (int id, UpdateUserRequest req, HttpContext ctx, UserService svc) =>
            (await svc.UpdateAsync(Actor.From(ctx.User), id, req)).ToHttp());
    }
}
