using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Features.Finance;

/// <param name="VatRate">Tutarın içindeki KDV payını hesaplamak için oran (%). Boşsa 0.</param>
public sealed record CashRequest(CashType Type, decimal? Amount, decimal? VatRate, string? Category, DateOnly? Date, string? Note);

public sealed record DebtRequest(DebtKind Kind, string? Party, decimal? Amount, decimal? OpeningPaid, DateOnly? Date, DateOnly? DueDate, string? Note);

/// <param name="Record">true: ödeme kasaya gelir/gider olarak da yazılır.</param>
public sealed record PaymentRequest(decimal? Amount, DateOnly? Date, bool Record = true);

/// <summary>Gelir/gider satırı. Linked = otomatik oluştu, elle değiştirilemez.</summary>
public sealed record CashDto(int Id, CashType Type, decimal Amount, decimal VatAmount, string Category, DateOnly Date, string Note,
    string CreatedBy, int? SaleId, bool Linked);

public sealed record DebtPaymentDto(int Id, decimal Amount, DateOnly Date, string CreatedBy);

public sealed record DebtDto(int Id, DebtKind Kind, string Party, int? SupplierId, decimal Amount, decimal OpeningPaid, decimal Paid,
    decimal Remaining, DateOnly Date, DateOnly? DueDate, string Note, int? SaleId, string? SaleNo, bool Linked,
    IEnumerable<DebtPaymentDto> Payments)
{
    /// <summary>Payments yüklenmiş bir Debt'ten üretir.</summary>
    public static DebtDto From(Debt d) => new(
        d.Id, d.Kind, d.Party, d.SupplierId, d.Amount, d.OpeningPaid, d.Paid, d.Remaining, d.Date, d.DueDate, d.Note,
        d.SaleId, d.SaleId is int s ? Format.SaleNo(s) : null, d.IsLinked,
        d.Payments.OrderBy(p => p.Date).ThenBy(p => p.Id).Select(p => new DebtPaymentDto(p.Id, p.Amount, p.Date, p.CreatedBy)));
}
