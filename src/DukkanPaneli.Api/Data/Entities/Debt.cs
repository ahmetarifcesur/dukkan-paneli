using System.Text.Json.Serialization;

namespace DukkanPaneli.Api.Data;

/// <summary>Borç (dükkanın ödeyeceği) ya da alacak (dükkanın tahsil edeceği).</summary>
public class Debt
{
    public int Id { get; set; }
    public DebtKind Kind { get; set; }

    /// <summary>Karşı taraf: kişi ya da firma adı.</summary>
    public string Party { get; set; } = "";

    public int? SupplierId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Kayıt oluşurken zaten ödenmiş kısım (ör. eski sistemden devir).</summary>
    public decimal OpeningPaid { get; set; }

    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    // Veresiye satıştan ya da vadeli stok girişinden otomatik oluştuysa dolu olur.
    public int? SaleId { get; set; }
    public int? StockMovementId { get; set; }

    [JsonIgnore] public Sale? Sale { get; set; }
    [JsonIgnore] public StockMovement? StockMovement { get; set; }
    public List<DebtPayment> Payments { get; set; } = [];

    // Hesaplanan değerler (veritabanında sütun değildir; AppDb'de Ignore edilir).
    // Doğru sonuç için Payments yüklenmiş olmalı: .Include(d => d.Payments)
    public bool IsLinked => SaleId is not null || StockMovementId is not null;
    public decimal Paid => OpeningPaid + Payments.Sum(p => p.Amount);
    public decimal Remaining => Amount - Paid;
}

public class DebtPayment
{
    public int Id { get; set; }
    public int DebtId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}
