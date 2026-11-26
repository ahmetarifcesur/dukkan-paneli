using System.Text.Json.Serialization;

namespace DukkanPaneli.Api.Data;

/// <summary>Stoktaki her değişikliğin kaydı. Bir ürünün hareketlerinin toplamı güncel stoğa eşittir.</summary>
public class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public MovementType Type { get; set; }

    /// <summary>İşaretli miktar: giriş (+), çıkış (−).</summary>
    public decimal Quantity { get; set; }

    /// <summary>Hareketten sonraki stok (geçmişi okumayı kolaylaştırır).</summary>
    public decimal StockAfter { get; set; }

    public decimal UnitCost { get; set; }
    public string Note { get; set; } = "";
    public int? SaleId { get; set; }

    [JsonIgnore] public Sale? Sale { get; set; }
    [JsonIgnore] public Product? Product { get; set; }
}
