namespace DukkanPaneli.Api.Data;

public class Sale
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public PaymentType PaymentType { get; set; }
    public string Customer { get; set; } = "";

    /// <summary>İndirim öncesi toplam (KDV dahil).</summary>
    public decimal Gross { get; set; }
    public decimal Discount { get; set; }

    /// <summary>Ödenecek toplam (KDV dahil) = Gross − Discount.</summary>
    public decimal Total { get; set; }
    public decimal VatTotal { get; set; }

    /// <summary>Satılan malın alış maliyeti. Brüt kâr = Total − CostTotal.</summary>
    public decimal CostTotal { get; set; }

    public string Note { get; set; } = "";
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";
    public List<SaleLine> Lines { get; set; } = [];
}

/// <summary>
/// Satış kalemi. Ürün adı, fiyatı ve maliyeti satış anındaki hâliyle kopyalanır;
/// ürün sonradan değişse de eski fişler aynı kalır.
/// </summary>
public class SaleLine
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal LineTotal { get; set; }
    public decimal UnitCost { get; set; }
}
