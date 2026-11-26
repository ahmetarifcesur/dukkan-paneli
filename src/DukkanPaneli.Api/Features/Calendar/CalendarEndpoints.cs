using DukkanPaneli.Api.Common;

namespace DukkanPaneli.Api.Features.Calendar;

public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/events").RequireAuthorization();

        events.MapPost("/", async (EventRequest r, HttpContext ctx, CalendarService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), r)).ToHttp());
        events.MapPut("/{id:int}", async (int id, EventRequest r, HttpContext ctx, CalendarService svc) =>
            (await svc.UpdateAsync(Actor.From(ctx.User), id, r)).ToHttp());
        events.MapDelete("/{id:int}", async (int id, HttpContext ctx, CalendarService svc) =>
            (await svc.DeleteAsync(Actor.From(ctx.User), id)).ToHttp());
    }
}
