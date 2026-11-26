using System.Text.Json.Serialization;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Features.Reports;

// Rapor yanıtı. JSON adları arayüzle (views-admin.js → renderReports) ortaktır;
// Türkçe kalan iki ad için [JsonPropertyName] kullanılıyor.

public sealed record ReportDto(
    DateOnly From, DateOnly To,
    CashTotals Totals,
    IEnumerable<CategoryTotal> ByCategory,
    SalesSummary Sales,
    IEnumerable<PaymentTotal> Payments,
    IEnumerable<TopProduct> TopProducts,
    IEnumerable<SellerTotal> BySeller,
    VatSummary Vat,
    LossSummary Losses,
    IEnumerable<DailyTotal> Daily);

public sealed record CashTotals([property: JsonPropertyName("gelir")] decimal Income, [property: JsonPropertyName("gider")] decimal Expense, decimal Net, int Count);

public sealed record CategoryTotal(CashType Type, string Category, decimal Total, int Count);

/// <param name="Margin">Brüt kâr marjı (%).</param>
public sealed record SalesSummary(int Count, int Cancelled, decimal Revenue, decimal Discount, decimal Cost, decimal GrossProfit, decimal Margin, decimal AvgBasket);

public sealed record PaymentTotal(PaymentType Type, int Count, decimal Total);

public sealed record TopProduct(int ProductId, string Name, string Unit, decimal Quantity, decimal Revenue, decimal Profit);

public sealed record SellerTotal(string Name, int Count, decimal Total);

/// <param name="Output">Satışlarda hesaplanan KDV.</param>
/// <param name="Input">Alış ve giderlerde ödenen (indirilecek) KDV.</param>
/// <param name="Payable">Tahmini ödenecek KDV = Output − Input.</param>
public sealed record VatSummary(decimal Output, decimal Input, decimal Payable);

public sealed record LossSummary(int Count, decimal Value);

public sealed record DailyTotal(DateOnly Date, [property: JsonPropertyName("gelir")] decimal Income, [property: JsonPropertyName("gider")] decimal Expense);
