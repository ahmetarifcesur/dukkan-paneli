using System.Globalization;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Auth;
using DukkanPaneli.Api.Features.Calendar;
using DukkanPaneli.Api.Features.Catalog;
using DukkanPaneli.Api.Features.Finance;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Dashboard;

public sealed record ShopSettingsDto(string ShopName, decimal DefaultVatRate);

/// <summary>
/// Arayüzün açılışta (ve her değişiklikten sonra) tek istekte aldığı veriler. Cash ve Debts çalışan için null'dur.
/// Cash sadece <see cref="CashFrom"/> tarihinden sonraki kayıtları içerir; daha eski bir dönem açılınca
/// arayüz "?cashFrom=" ile pencereyi genişletir. Böylece yıllar geçtikçe yanıt büyümez.
/// </summary>
public sealed record BootstrapDto(MeDto Me, ShopSettingsDto Settings, IEnumerable<ProductDto> Products, IEnumerable<SupplierDto> Suppliers,
    List<CalendarEvent> Events, List<CashDto>? Cash, DateOnly CashFrom, List<DebtDto>? Debts);

/// <param name="Key">Aynı bildirimi iki kez göstermemek için tarayıcı bu anahtarı hatırlar.</param>
/// <param name="Level">info / warn / danger</param>
/// <param name="Link">Tıklanınca açılacak ekran, ör. "#urunler".</param>
public sealed record NotificationDto(string Key, string Level, string Title, string Body, string Link);

public sealed class DashboardService(AppDb db, CalendarService calendar, CashService cash, DebtService debts)
{
    /// <summary>Varsayılan gelir-gider penceresi: 12 ay önceki ayın ilk günü (genel bakış ve geçen yılla karşılaştırma için yeterli).</summary>
    public static DateOnly DefaultCashFrom(DateOnly today) => new DateOnly(today.Year, today.Month, 1).AddMonths(-12);

    public async Task<BootstrapDto> BootstrapAsync(Actor actor, DateOnly? cashFrom = null)
    {
        var defaultFrom = DefaultCashFrom(Clock.Today);
        var from = cashFrom is { } f && f < defaultFrom ? f : defaultFrom;
        var me = await db.Users.AsNoTracking().Where(u => u.Id == actor.Id).Select(u => MeDto.From(u)).FirstAsync();
        var settings = await db.ReadSettingsAsync();
        var products = await db.Products.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => !s.IsArchived).OrderBy(s => s.Name).ToListAsync();

        return new BootstrapDto(
            me,
            new ShopSettingsDto(
                settings.GetValueOrDefault(Setting.ShopName, "Dükkanım"),
                decimal.TryParse(settings.GetValueOrDefault(Setting.DefaultVatRate), NumberStyles.Number, CultureInfo.InvariantCulture, out var vat) ? vat : 20),
            products.Select(p => ProductDto.From(p, showCost: actor.IsOwner)),
            suppliers.Select(SupplierDto.From),
            await calendar.ListAsync(),
            actor.IsOwner ? await cash.ListAsync(from) : null,
            from,
            actor.IsOwner ? await debts.ListAsync() : null);
    }

    /// <summary>Kritik stok, bugün/yarın etkinlikleri ve (sahip için) 3 gün içinde vadesi gelen ödemeler.</summary>
    public async Task<List<NotificationDto>> NotificationsAsync(Actor actor)
    {
        var today = Clock.Today;
        var list = new List<NotificationDto>();

        var products = await db.Products.AsNoTracking().Where(p => !p.IsArchived).ToListAsync();
        foreach (var p in products.Where(p => p.Stock <= p.MinStock))
            list.Add(new($"low-{p.Id}-{p.Stock}", "warn", "Kritik stok",
                $"{p.Name}: {Format.Qty(p.Stock)} {p.Unit} kaldı (min. {Format.Qty(p.MinStock)})", "#urunler"));

        var events = await db.Events.AsNoTracking().Where(e => e.Date >= today && e.Date <= today.AddDays(1)).OrderBy(e => e.Date).ToListAsync();
        foreach (var e in events)
            list.Add(new($"ev-{e.Id}-{e.Date}", "info", e.Date == today ? "Bugün" : "Yarın",
                $"{e.Title}{(e.Time.Length > 0 ? " · " + e.Time : "")}", "#takvim"));

        if (actor.IsOwner)
        {
            var due = await db.Debts.AsNoTracking().Include(d => d.Payments)
                .Where(d => d.DueDate != null && d.DueDate <= today.AddDays(3)).ToListAsync();
            foreach (var d in due.Where(d => d.Remaining > 0))
            {
                var days = d.DueDate!.Value.DayNumber - today.DayNumber;
                var what = d.Kind == DebtKind.Payable ? "Ödeme" : "Tahsilat";
                list.Add(new(
                    $"due-{d.Id}-{d.DueDate}-{(days < 0 ? "late" : days.ToString())}",
                    days < 0 ? "danger" : "warn",
                    days < 0 ? $"{what} gecikti" : days == 0 ? $"{what} bugün" : $"{what} {days} gün sonra",
                    $"{d.Party}: {Format.Money(d.Remaining)}",
                    "#borclar"));
            }
        }
        return list;
    }
}
