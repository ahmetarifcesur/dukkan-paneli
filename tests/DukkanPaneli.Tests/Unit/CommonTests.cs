using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Tests.Unit;

/// <summary>Birim testleri: veritabanı ya da HTTP yok, sadece saf fonksiyonlar. Milisaniyeler içinde çalışır.</summary>
public class CommonTests
{
    [Theory]
    [InlineData(120, 20, 20)]      // %20 KDV dahil 120 ₺ → 20 ₺ KDV
    [InlineData(101, 1, 1)]        // %1
    [InlineData(110, 10, 10)]      // %10
    [InlineData(100, 0, 0)]        // KDV yok
    [InlineData(10.99, 20, 1.83)]  // kuruşa yuvarlanır
    public void VatOf_kdv_dahil_tutardan_kdv_payini_bulur(decimal gross, decimal rate, decimal expected) =>
        Assert.Equal(expected, MoneyMath.VatOf(gross, rate));

    [Theory]
    [InlineData(2.345, 2.35)]  // yarım kuruş yukarı (bankacı yuvarlaması 2.34 verirdi)
    [InlineData(2.344, 2.34)]
    [InlineData(-2.345, -2.35)]
    public void Round_yarimi_yukari_yuvarlar(decimal value, decimal expected) =>
        Assert.Equal(expected, MoneyMath.Round(value));

    [Fact]
    public void DateRange_bos_birakilinca_bu_ayi_verir()
    {
        var r = DateRange.Of(null, null);
        Assert.Equal(1, r.From.Day);
        Assert.Equal(r.From.AddMonths(1).AddDays(-1), r.To);
    }

    [Fact]
    public void DateRange_ters_verilirse_yer_degistirir()
    {
        var a = new DateOnly(2026, 5, 20);
        var b = new DateOnly(2026, 5, 1);
        Assert.Equal(new DateRange(b, a), DateRange.Of(a, b));
    }

    [Theory]
    [InlineData("Satis", MovementType.Sale)]       // JSON (Türkçe) adı
    [InlineData("satis", MovementType.Sale)]       // büyük/küçük harf duyarsız
    [InlineData("Waste", MovementType.Waste)]      // kod adı da kabul
    [InlineData("SatisIptal", MovementType.SaleCancel)]
    public void EnumWire_turkce_ve_kod_adini_cozer(string text, MovementType expected)
    {
        Assert.True(EnumWire.TryParse<MovementType>(text, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void EnumWire_bilinmeyen_degeri_reddeder() =>
        Assert.False(EnumWire.TryParse<MovementType>("Yok", out _));

    [Fact]
    public void Format_turkce_para_ve_fis_no()
    {
        Assert.Equal("1.234,5 ₺", Format.Money(1234.5m));
        Assert.Equal("F-000042", Format.SaleNo(42));
        Assert.Equal("Veresiye", Format.Label(PaymentType.OnCredit));
    }

    [Fact]
    public void Input_Clean_kirpar_ve_sinirlar()
    {
        Assert.Equal("abc", Input.Clean("  abcdef ", 3));
        Assert.Equal("", Input.Clean(null));
        Assert.Equal("adet", Input.CleanOr("   ", 10, "adet"));
    }

    [Fact]
    public void Debt_kalan_tutari_hesaplar()
    {
        var d = new Debt { Amount = 1000, OpeningPaid = 200, Payments = [new() { Amount = 300 }, new() { Amount = 100 }] };
        Assert.Equal(600, d.Paid);
        Assert.Equal(400, d.Remaining);
    }
}
