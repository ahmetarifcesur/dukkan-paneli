using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Features.Sales;

/// <param name="UnitPrice">Boşsa ürünün satış fiyatı kullanılır.</param>
public sealed record SaleLineRequest(int ProductId, decimal? Quantity, decimal? UnitPrice);

public sealed record SaleRequest(DateOnly? Date, PaymentType PaymentType, string? Customer, DateOnly? DueDate,
    decimal? Discount, string? Note, List<SaleLineRequest>? Lines);

public sealed record CancelRequest(string? Reason);

public sealed record SaleLineDto(int Id, int ProductId, string ProductName, string Unit, decimal Quantity, decimal UnitPrice,
    decimal VatRate, decimal LineTotal, decimal Vat);

/// <summary>Satış (fiş). CostTotal yalnızca sahibe gönderilir.</summary>
public sealed record SaleDto(int Id, string No, DateOnly Date, DateTime CreatedAt, string CreatedBy, PaymentType PaymentType,
    string Customer, decimal Gross, decimal Discount, decimal Total, decimal VatTotal, decimal? CostTotal, string Note,
    bool IsCancelled, DateTime? CancelledAt, string CancelReason, IEnumerable<SaleLineDto> Lines)
{
    public static SaleDto From(Sale s, bool showCost) => new(
        s.Id, Format.SaleNo(s.Id), s.Date, s.CreatedAt, s.CreatedBy, s.PaymentType, s.Customer, s.Gross, s.Discount, s.Total, s.VatTotal,
        showCost ? s.CostTotal : null, s.Note, s.IsCancelled, s.CancelledAt, s.CancelReason,
        s.Lines.OrderBy(l => l.Id).Select(l => new SaleLineDto(l.Id, l.ProductId, l.ProductName, l.Unit, l.Quantity, l.UnitPrice,
            l.VatRate, l.LineTotal, MoneyMath.VatOf(l.LineTotal, l.VatRate))));
}
