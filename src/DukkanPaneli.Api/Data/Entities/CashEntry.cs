using System.Text.Json.Serialization;

namespace DukkanPaneli.Api.Data;

/// <summary>Kasaya giren (gelir) ya da çıkan (gider) para. Nakit esası: para el değiştirdiğinde yazılır.</summary>
public class CashEntry
{
    public int Id { get; set; }
    public CashType Type { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Tutarın içindeki KDV payı.</summary>
    public decimal VatAmount { get; set; }

    public string Category { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";

    // Bağlı kayıtlar: bu alanlardan biri doluysa kayıt başka bir işlemden otomatik oluşmuştur
    // ve elle düzenlenemez/silinemez (bkz. CashService.LockReason).
    public int? SaleId { get; set; }
    public int? DebtPaymentId { get; set; }
    public int? StockMovementId { get; set; }

    [JsonIgnore] public Sale? Sale { get; set; }
    [JsonIgnore] public DebtPayment? DebtPayment { get; set; }
    [JsonIgnore] public StockMovement? StockMovement { get; set; }
}
