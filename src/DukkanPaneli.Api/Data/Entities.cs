using System.Text.Json.Serialization;

namespace DukkanPaneli.Api.Data;

public enum Role { Sahip, Calisan }
public enum CashType { Gelir, Gider }
public enum PaymentType { Nakit, Kart, Veresiye }
public enum DebtKind { Borc, Alacak }

/// <summary>Stok hareket türleri. Miktar işaretlidir: giriş (+), çıkış (-).</summary>
public enum MovementType { Acilis, Giris, Satis, SatisIptal, Fire, Iade, Sayim, Duzeltme }

public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Şifre/rol/aktiflik değişince yenilenir; eski oturumlar geçersiz olur.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; }
}

public class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsArchived { get; set; }
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "adet";
    public decimal Stock { get; set; }
    public decimal Capacity { get; set; }
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
    /// <summary>Ödenecek toplam (KDV dahil).</summary>
    public decimal Total { get; set; }
    public decimal VatTotal { get; set; }
    public decimal CostTotal { get; set; }
    public string Note { get; set; } = "";
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = "";
    public List<SaleLine> Lines { get; set; } = [];
}

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

public class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public MovementType Type { get; set; }
    public decimal Quantity { get; set; }
    public decimal StockAfter { get; set; }
    public decimal UnitCost { get; set; }
    public string Note { get; set; } = "";
    public int? SaleId { get; set; }
    [JsonIgnore] public Sale? Sale { get; set; }
    [JsonIgnore] public Product? Product { get; set; }
}

public class CashEntry
{
    public int Id { get; set; }
    public CashType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public string Category { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    // Bağlı kayıtlar: bu alanlardan biri doluysa kayıt otomatik oluşmuştur ve elle silinemez.
    public int? SaleId { get; set; }
    public int? DebtPaymentId { get; set; }
    public int? StockMovementId { get; set; }
    [JsonIgnore] public Sale? Sale { get; set; }
    [JsonIgnore] public DebtPayment? DebtPayment { get; set; }

    [JsonIgnore] public StockMovement? StockMovement { get; set; }
}

public class Debt
{
    public int Id { get; set; }
    public DebtKind Kind { get; set; }
    public string Party { get; set; } = "";
    public int? SupplierId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Kayıt oluşurken zaten ödenmiş kısım (ör. eski sistemden devir).</summary>
    public decimal OpeningPaid { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? SaleId { get; set; }
    public int? StockMovementId { get; set; }
    [JsonIgnore] public Sale? Sale { get; set; }
    [JsonIgnore] public StockMovement? StockMovement { get; set; }
    public List<DebtPayment> Payments { get; set; } = [];
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

public class CalendarEvent
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Time { get; set; } = "";
    public string Type { get; set; } = "diger";
    public string Note { get; set; } = "";
}

/// <summary>Her değişikliğin kaydı. Changes: önce/sonra görüntüleri (JSON) — geri almada kullanılır.</summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Changes { get; set; } = "[]";
    public bool Undoable { get; set; } = true;
    public DateTime? UndoneAt { get; set; }
    public int? UndoneByAuditId { get; set; }
    public int? UndoOfAuditId { get; set; }
}
