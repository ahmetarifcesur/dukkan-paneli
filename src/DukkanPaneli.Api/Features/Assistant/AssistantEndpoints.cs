using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.Assistant;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        // Dükkanın tüm mali özeti gönderildiği için sadece sahip kullanabilir.
        var assistant = app.MapGroup("/api/assistant").RequireAuthorization(AuthSetup.OwnerPolicy);

        assistant.MapGet("/status", (AssistantService svc) => Results.Ok(svc.Status()));

        assistant.MapGet("/context", async (AssistantService svc) => Results.Ok(new { text = await svc.PreviewAsync() }));

        assistant.MapPost("/ask", async (AskRequest r, AssistantService svc, HttpContext ctx) =>
        {
            var result = await svc.AskAsync(r, ctx.RequestAborted);
            return result.Succeeded ? Results.Ok(result.Value) : HttpResults.Fail(result.Error!, result.Status);
        }).RequireRateLimiting(AuthSetup.AssistantRateLimit);
    }
}
