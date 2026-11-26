namespace DukkanPaneli.Api.Common;

/// <summary>Para hesapları. Para için her zaman <c>decimal</c> kullanılır (double yuvarlama hatası yapar).</summary>
public static class MoneyMath
{
    /// <summary>Kuruşa yuvarlar. 0,5 kuruş yukarı yuvarlanır (bankacı yuvarlaması değil).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// KDV dahil tutarın içindeki KDV payı.
    /// Örnek: %20 KDV'li 120 ₺ → 120 × 20 / 120 = 20 ₺ KDV.
    /// </summary>
    public static decimal VatOf(decimal grossAmount, decimal ratePercent) =>
        ratePercent <= 0 ? 0 : Round(grossAmount * ratePercent / (100 + ratePercent));
}
