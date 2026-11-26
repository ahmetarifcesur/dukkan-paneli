using DukkanPaneli.Api.Common;

namespace DukkanPaneli.Api.Features.Dashboard;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/bootstrap", async (DateOnly? cashFrom, HttpContext ctx, DashboardService svc) =>
            Results.Ok(await svc.BootstrapAsync(Actor.From(ctx.User), cashFrom)));

        api.MapGet("/notifications", async (HttpContext ctx, DashboardService svc) =>
            Results.Ok(await svc.NotificationsAsync(Actor.From(ctx.User))));
    }
}
