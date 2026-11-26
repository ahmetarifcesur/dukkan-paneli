using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.Finance;

/// <summary>Gelir-gider ve borç-alacak. Tamamı sahibe özel.</summary>
public static class FinanceEndpoints
{
    public static void MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var owner = app.MapGroup("/api").RequireAuthorization(AuthSetup.OwnerPolicy);

        owner.MapPost("/cash", async (CashRequest r, HttpContext ctx, CashService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), r)).ToHttp());
        owner.MapPut("/cash/{id:int}", async (int id, CashRequest r, HttpContext ctx, CashService svc) =>
            (await svc.UpdateAsync(Actor.From(ctx.User), id, r)).ToHttp());
        owner.MapDelete("/cash/{id:int}", async (int id, HttpContext ctx, CashService svc) =>
            (await svc.DeleteAsync(Actor.From(ctx.User), id)).ToHttp());

        owner.MapPost("/debts", async (DebtRequest r, HttpContext ctx, DebtService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), r)).ToHttp());
        owner.MapPut("/debts/{id:int}", async (int id, DebtRequest r, HttpContext ctx, DebtService svc) =>
            (await svc.UpdateAsync(Actor.From(ctx.User), id, r)).ToHttp());
        owner.MapDelete("/debts/{id:int}", async (int id, HttpContext ctx, DebtService svc) =>
            (await svc.DeleteAsync(Actor.From(ctx.User), id)).ToHttp());

        owner.MapPost("/debts/{id:int}/payments", async (int id, PaymentRequest r, HttpContext ctx, DebtService svc) =>
            (await svc.AddPaymentAsync(Actor.From(ctx.User), id, r)).ToHttp());
        owner.MapDelete("/debts/{id:int}/payments/{paymentId:int}", async (int id, int paymentId, HttpContext ctx, DebtService svc) =>
            (await svc.DeletePaymentAsync(Actor.From(ctx.User), id, paymentId)).ToHttp());
    }
}
