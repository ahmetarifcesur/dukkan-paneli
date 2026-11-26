using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.History;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var history = app.MapGroup("/api/history").RequireAuthorization(AuthSetup.OwnerPolicy);

        history.MapGet("/", async (DateOnly? from, DateOnly? to, string? user, string? q, int? page, HistoryService svc) =>
            Results.Ok(await svc.ListAsync(from, to, user, q, page)));

        history.MapGet("/{id:int}", async (int id, HistoryService svc) =>
            HttpResults.OkOrNotFound(await svc.DetailAsync(id), "Kayıt bulunamadı."));

        history.MapPost("/{id:int}/undo", async (int id, HttpContext ctx, HistoryService svc) =>
            (await svc.UndoAsync(Actor.From(ctx.User), id)).ToHttp());
    }
}
