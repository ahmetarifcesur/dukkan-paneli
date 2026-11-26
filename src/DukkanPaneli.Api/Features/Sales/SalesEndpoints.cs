using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.Sales;

public static class SalesEndpoints
{
    public static void MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var sales = app.MapGroup("/api/sales").RequireAuthorization();

        sales.MapPost("/", async (SaleRequest r, HttpContext ctx, SaleService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), r)).ToHttp());

        sales.MapGet("/", async (DateOnly? from, DateOnly? to, HttpContext ctx, SaleService svc) =>
            Results.Ok(await svc.ListAsync(DateRange.Of(from, to), Actor.From(ctx.User).IsOwner)));

        sales.MapGet("/{id:int}", async (int id, HttpContext ctx, SaleService svc) =>
            HttpResults.OkOrNotFound(await svc.GetAsync(id, Actor.From(ctx.User).IsOwner), "Satış bulunamadı."));

        sales.MapPost("/{id:int}/cancel", async (int id, CancelRequest r, HttpContext ctx, SaleService svc) =>
            (await svc.CancelAsync(Actor.From(ctx.User), id, r)).ToHttp())
            .RequireAuthorization(AuthSetup.OwnerPolicy);
    }
}
