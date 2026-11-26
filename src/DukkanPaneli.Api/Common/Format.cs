using System.Globalization;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Common;

/// <summary>Geçmiş özetlerinde ve Excel'de kullanılan Türkçe metin biçimleri.</summary>
public static class Format
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>1234.5 → "1.234,5 ₺"</summary>
    public static string Money(decimal v) => v.ToString("#,##0.##", Turkish) + " ₺";

    /// <summary>2.500 → "2,5"</summary>
    public static string Qty(decimal v) => v.ToString("#,##0.###", Turkish);

    /// <summary>Fiş numarası: 42 → "F-000042"</summary>
    public static string SaleNo(int id) => $"F-{id:D6}";

    public static string Label(MovementType t) => t switch
    {
        MovementType.Opening => "Açılış",
        MovementType.StockIn => "Giriş",
        MovementType.Sale => "Satış",
        MovementType.SaleCancel => "Satış iptali",
        MovementType.Waste => "Fire / zayi",
        MovementType.ReturnToSupplier => "Tedarikçiye iade",
        MovementType.Count => "Sayım",
        MovementType.Correction => "Düzeltme",
        _ => t.ToString(),
    };

    public static string Label(PaymentType p) => p switch
    {
        PaymentType.Cash => "Nakit",
        PaymentType.Card => "Kart",
        _ => "Veresiye",
    };

    public static string Label(Role r) => r == Role.Owner ? "Sahip" : "Çalışan";
    public static string Label(CashType t) => t == CashType.Income ? "Gelir" : "Gider";
    public static string Label(DebtKind k) => k == DebtKind.Payable ? "Borç" : "Alacak";
}
