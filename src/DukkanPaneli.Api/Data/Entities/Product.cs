namespace DukkanPaneli.Api.Data;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "adet";

    /// <summary>
    /// Güncel stok. Doğrudan değiştirilmez: her değişiklik bir <see cref="StockMovement"/> ile birlikte yapılır,
    /// böylece "stok = hareketlerin toplamı" kuralı hep korunur.
    /// </summary>
    public decimal Stock { get; set; }

    /// <summary>Rafta/depoda tutulabilecek en fazla miktar.</summary>
    public decimal Capacity { get; set; }

    /// <summary>Stok bu seviyeye inince "kritik stok" uyarısı verilir.</summary>
    public decimal MinStock { get; set; }

    /// <summary>Son birim alış fiyatı (KDV dahil).</summary>
    public decimal Cost { get; set; }

    /// <summary>Birim satış fiyatı (KDV dahil).</summary>
    public decimal Price { get; set; }

    /// <summary>KDV oranı (%), ör. 1, 10, 20.</summary>
    public decimal VatRate { get; set; }

    public int? SupplierId { get; set; }
    public bool IsArchived { get; set; }
}
