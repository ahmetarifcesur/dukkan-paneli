namespace DukkanPaneli.Api.Common;

/// <summary>
/// Tarih yardımcıları. İş tarihleri (satış günü, vade) yerel takvim günüdür → DateOnly.
/// Zaman damgaları (CreatedAt vb.) ise her zaman UTC saklanır → DateTime.UtcNow.
/// </summary>
public static class Clock
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>Kapalı tarih aralığı [From, To].</summary>
public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    /// <summary>Boş bırakılan uçları tamamlar: varsayılan bu ay. Ters verilirse yer değiştirir.</summary>
    public static DateRange Of(DateOnly? from, DateOnly? to)
    {
        var today = Clock.Today;
        var f = from ?? new DateOnly(today.Year, today.Month, 1);
        var t = to ?? f.AddMonths(1).AddDays(-1);
        return f <= t ? new(f, t) : new(t, f);
    }
}
