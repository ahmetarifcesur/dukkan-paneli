using System.Text.Json.Serialization;

namespace DukkanPaneli.Api.Data;

// Koddaki adlar İngilizcedir. JSON'da (yani API sözleşmesinde) [JsonStringEnumMemberName] ile
// verilen Türkçe adlar kullanılır; arayüz bu değerlerle çalışır ("Gelir", "Veresiye" gibi).
// Veritabanında ise kod adı (ör. "Income") metin olarak saklanır — bkz. AppDb.ConfigureConventions.

public enum Role
{
    [JsonStringEnumMemberName("Sahip")] Owner,
    [JsonStringEnumMemberName("Calisan")] Staff,
}

public enum CashType
{
    [JsonStringEnumMemberName("Gelir")] Income,
    [JsonStringEnumMemberName("Gider")] Expense,
}

public enum PaymentType
{
    [JsonStringEnumMemberName("Nakit")] Cash,
    [JsonStringEnumMemberName("Kart")] Card,
    /// <summary>Veresiye: para sonra tahsil edilir, satış anında alacak kaydı oluşur.</summary>
    [JsonStringEnumMemberName("Veresiye")] OnCredit,
}

public enum DebtKind
{
    /// <summary>Borç: dükkanın başkasına ödeyeceği para.</summary>
    [JsonStringEnumMemberName("Borc")] Payable,
    /// <summary>Alacak: dükkanın başkasından tahsil edeceği para.</summary>
    [JsonStringEnumMemberName("Alacak")] Receivable,
}

/// <summary>Stok hareket türleri. Hareketin miktarı işaretlidir: giriş (+), çıkış (−).</summary>
public enum MovementType
{
    [JsonStringEnumMemberName("Acilis")] Opening,
    [JsonStringEnumMemberName("Giris")] StockIn,
    [JsonStringEnumMemberName("Satis")] Sale,
    [JsonStringEnumMemberName("SatisIptal")] SaleCancel,
    [JsonStringEnumMemberName("Fire")] Waste,
    [JsonStringEnumMemberName("Iade")] ReturnToSupplier,
    [JsonStringEnumMemberName("Sayim")] Count,
    [JsonStringEnumMemberName("Duzeltme")] Correction,
}
