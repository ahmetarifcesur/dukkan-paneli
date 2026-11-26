using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.Admin;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var owner = app.MapGroup("/api").RequireAuthorization(AuthSetup.OwnerPolicy);

        owner.MapPut("/settings", async (SettingsRequest r, HttpContext ctx, AdminService svc) =>
            (await svc.UpdateSettingsAsync(Actor.From(ctx.User), r)).ToHttp());

        owner.MapPost("/admin/demo", async (HttpContext ctx, AdminService svc) =>
            (await svc.LoadDemoAsync(Actor.From(ctx.User))).ToHttp());

        owner.MapPost("/admin/clear", async (HttpContext ctx, AdminService svc) =>
            (await svc.ClearAllAsync(Actor.From(ctx.User))).ToHttp());

        owner.MapGet("/admin/backups", (AdminService svc) => Results.Ok(svc.AutoBackups()));

        owner.MapGet("/admin/backup", async (AdminService svc) =>
        {
            var file = await svc.BackupAsync();
            return Results.File(file.Content, "application/vnd.sqlite3", file.FileName);
        });
    }
}
