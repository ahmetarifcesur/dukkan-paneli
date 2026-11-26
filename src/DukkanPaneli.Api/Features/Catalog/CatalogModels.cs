using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Features.Catalog;

public sealed record ProductRequest(string? Name, string? Category, string? Unit, decimal? Stock, decimal? Capacity, decimal? MinStock,
    decimal? Cost, decimal? Price, decimal? VatRate, int? SupplierId);

/// <param name="UnitCost">Boşsa ürünün son alış fiyatı kullanılır (çalışan maliyeti görmez).</param>
/// <param name="Credit">true: tedarikçiye vadeli borç yazılır; false: peşin gider yazılır.</param>
public sealed record StockInRequest(decimal? Quantity, decimal? UnitCost, int? SupplierId, DateOnly? Date, bool Credit, DateOnly? DueDate, string? Note);

/// <param name="Quantity">Fire/iade: çıkan miktar. Sayım: sayılan miktar. Düzeltme: +/− fark.</param>
public sealed record AdjustRequest(MovementType Type, decimal? Quantity, string? Note, DateOnly? Date);

public sealed record SupplierRequest(string? Name, string? Phone, string? Email, string? Note);

/// <summary>Ürün. Cost yalnızca sahibe gönderilir (çalışan için null).</summary>
public sealed record ProductDto(int Id, string Name, string Category, string Unit, decimal Stock, decimal Capacity, decimal MinStock,
    decimal? Cost, decimal Price, decimal VatRate, int? SupplierId)
{
    public static ProductDto From(Product p, bool showCost) =>
        new(p.Id, p.Name, p.Category, p.Unit, p.Stock, p.Capacity, p.MinStock, showCost ? p.Cost : null, p.Price, p.VatRate, p.SupplierId);
}

public sealed record SupplierDto(int Id, string Name, string Phone, string Email, string Note)
{
    public static SupplierDto From(Supplier s) => new(s.Id, s.Name, s.Phone, s.Email, s.Note);
}

/// <summary>Bir ürünün hareket listesindeki satır.</summary>
public sealed record ProductMovementDto(int Id, DateOnly Date, DateTime CreatedAt, string CreatedBy, MovementType Type, string TypeLabel,
    decimal Quantity, decimal StockAfter, decimal? UnitCost, string Note, int? SaleId, string? SaleNo);

public sealed record ProductMovementsDto(ProductBrief Product, decimal? StockAt, DateOnly? At, IEnumerable<ProductMovementDto> Movements);

public sealed record ProductBrief(int Id, string Name, string Unit, decimal Stock);

/// <summary>Tüm ürünlerin hareket listesindeki satır.</summary>
public sealed record MovementDto(int Id, DateOnly Date, DateTime CreatedAt, string CreatedBy, int ProductId, string? Product, string? Unit,
    MovementType Type, string TypeLabel, decimal Quantity, decimal StockAfter, string Note, string? SaleNo);

internal static class MovementText
{
    public static string? SaleNo(int? saleId) => saleId is int s ? Format.SaleNo(s) : null;
}
