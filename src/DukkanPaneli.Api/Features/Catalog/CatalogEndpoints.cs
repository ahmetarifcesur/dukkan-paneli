using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.Catalog;

/// <summary>Ürün, stok ve tedarikçi uç noktaları.</summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var all = app.MapGroup("/api").RequireAuthorization();                        // giriş yapmış herkes
        var owner = app.MapGroup("/api").RequireAuthorization(AuthSetup.OwnerPolicy); // sadece sahip

        // ---- Ürünler ----
        owner.MapPost("/products", async (ProductRequest r, HttpContext ctx, ProductService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), r)).ToHttp());

        owner.MapPut("/products/{id:int}", async (int id, ProductRequest r, HttpContext ctx, ProductService svc) =>
            (await svc.UpdateAsync(Actor.From(ctx.User), id, r)).ToHttp());

        owner.MapDelete("/products/{id:int}", async (int id, HttpContext ctx, ProductService svc) =>
            (await svc.ArchiveAsync(Actor.From(ctx.User), id)).ToHttp());

        // ---- Stok ----
        all.MapPost("/products/{id:int}/stock-in", async (int id, StockInRequest r, HttpContext ctx, StockService svc) =>
            (await svc.StockInAsync(Actor.From(ctx.User), id, r)).ToHttp());

        all.MapPost("/products/{id:int}/adjust", async (int id, AdjustRequest r, HttpContext ctx, StockService svc) =>
            (await svc.AdjustAsync(Actor.From(ctx.User), id, r)).ToHttp());

        all.MapGet("/products/{id:int}/movements", async (int id, DateOnly? at, HttpContext ctx, StockService svc) =>
            HttpResults.OkOrNotFound(await svc.ProductMovementsAsync(id, at, Actor.From(ctx.User).IsOwner), "Ürün bulunamadı."));

        all.MapGet("/movements", async (DateOnly? from, DateOnly? to, string? type, StockService svc) =>
        {
            MovementType? filter = null;
            if (!string.IsNullOrEmpty(type))
            {
                if (!EnumWire.TryParse<MovementType>(type, out var t)) return HttpResults.Fail("Geçersiz hareket türü.");
                filter = t;
            }
            return Results.Ok(await svc.MovementsAsync(DateRange.Of(from, to), filter));
        });

        // ---- Tedarikçiler ----
        owner.MapPost("/suppliers", async (SupplierRequest r, HttpContext ctx, SupplierService svc) =>
            (await svc.CreateAsync(Actor.From(ctx.User), r)).ToHttp());

        owner.MapPut("/suppliers/{id:int}", async (int id, SupplierRequest r, HttpContext ctx, SupplierService svc) =>
            (await svc.UpdateAsync(Actor.From(ctx.User), id, r)).ToHttp());

        owner.MapDelete("/suppliers/{id:int}", async (int id, HttpContext ctx, SupplierService svc) =>
            (await svc.ArchiveAsync(Actor.From(ctx.User), id)).ToHttp());
    }
}
