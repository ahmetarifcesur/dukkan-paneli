using System.Globalization;
using System.Text;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Auth;
using DukkanPaneli.Api.Features.Reports;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Assistant;

/// <summary>
/// Dükkanın güncel durumunu modelin okuyacağı kısa bir metne çevirir. Hesapların çoğu raporlarla aynıdır
/// (<see cref="ReportService.Build"/>), böylece asistanın söylediği rakamlar ekrandakilerle tutar.
///
/// Gizlilik: müşteri adları, çalışan adları ve tedarikçi olmayan kişiler takma adla yazılır (<see cref="Pseudonyms"/>).
/// Telefon, e-posta, notlar ve fiş ayrıntıları hiç gönderilmez. Ürün ve tedarikçi firma adları gönderilir.
/// </summary>
public sealed class ShopSnapshotService(AppDb db, ReportService reports, UserService users)
{
    private static readonly CultureInfo Tr = Format.Turkish;
    private const int MaxProducts = 150;

    public async Task<ShopSnapshot> BuildAsync()
    {
        var names = new Pseudonyms();
        var today = Clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var lastMonthSameDay = lastMonthStart.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(lastMonthStart.Year, lastMonthStart.Month)) - 1);
        var last30 = today.AddDays(-29);

        var thisMonth = await reports.BuildAsync(new DateRange(monthStart, today));
        var lastMonthToDate = await reports.BuildAsync(new DateRange(lastMonthStart, lastMonthSameDay));
        var lastMonth = await reports.BuildAsync(new DateRange(lastMonthStart, monthStart.AddDays(-1)));
        var todayReport = await reports.BuildAsync(new DateRange(today, today));
        var recent = await reports.BuildAsync(new DateRange(last30, today));

        var sb = new StringBuilder();
        var settings = await db.ReadSettingsAsync();
        sb.AppendLine($"# Dükkan özeti — {today.ToString("d MMMM yyyy, dddd", Tr)}");
        sb.AppendLine($"Dükkan: {settings.GetValueOrDefault(Setting.ShopName, "Dükkan")}");
        sb.AppendLine("Para birimi TL. Kasa rakamları nakit esasına göredir: veresiye satış tahsil edilince, vadeli alım ödenince gelir/gider olur.");
        sb.AppendLine();

        // ---- Kasa ----
        sb.AppendLine("## Kasa (gelir / gider)");
        sb.AppendLine($"- Bu ay (1-{today.Day} {Month(today)}): {Totals(thisMonth)}");
        sb.AppendLine($"- Geçen ayın aynı dönemi (1-{lastMonthSameDay.Day} {Month(lastMonthStart)}): {Totals(lastMonthToDate)}");
        sb.AppendLine($"- Geçen ayın tamamı ({Month(lastMonthStart)}): {Totals(lastMonth)}");
        sb.AppendLine("- Son 6 ay:");
        for (var i = 5; i >= 0; i--)
        {
            var from = monthStart.AddMonths(-i);
            var to = i == 0 ? today : from.AddMonths(1).AddDays(-1);
            var r = i == 0 ? thisMonth : await reports.BuildAsync(new DateRange(from, to));
            sb.AppendLine($"  - {Month(from)} {from.Year}{(i == 0 ? " (devam ediyor)" : "")}: {Totals(r)}");
        }
        var expenseCats = thisMonth.ByCategory.Where(c => c.Type == CashType.Expense).Take(8).ToList();
        if (expenseCats.Count > 0)
            sb.AppendLine($"- Bu ayın gider kalemleri: {string.Join(", ", expenseCats.Select(c => $"{c.Category} {Format.Money(c.Total)}"))}");
        sb.AppendLine();

        // ---- Satış ----
        sb.AppendLine("## Satışlar (iptaller hariç)");
        sb.AppendLine($"- Bugün: {SalesLine(todayReport)}");
        sb.AppendLine($"- Bu ay: {SalesLine(thisMonth)}");
        sb.AppendLine($"- Geçen ayın aynı dönemi: {SalesLine(lastMonthToDate)}");
        sb.AppendLine($"- Geçen ayın tamamı: {SalesLine(lastMonth)}");
        if (thisMonth.Payments.Any())
            sb.AppendLine($"- Bu ay ödeme türleri: {string.Join(", ", thisMonth.Payments.Select(p => $"{Format.Label(p.Type)} {p.Count} satış {Format.Money(p.Total)}"))}");
        sb.AppendLine("- Son 30 günde en çok ciro yapan ürünler:");
        foreach (var p in recent.TopProducts)
            sb.AppendLine($"  - {p.Name}: {Format.Qty(p.Quantity)} {p.Unit}, ciro {Format.Money(p.Revenue)}, brüt kâr {Format.Money(p.Profit)}");
        sb.AppendLine($"- Son 30 gün fire/zayi: {recent.Losses.Count} kayıt, maliyeti {Format.Money(recent.Losses.Value)}");
        sb.AppendLine();

        // ---- Stok ----
        var products = await db.Products.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync();
        var suppliers = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        var sold30 = (await db.StockMovements.AsNoTracking()
                .Where(m => m.Date >= last30 && (m.Type == MovementType.Sale || m.Type == MovementType.SaleCancel))
                .Select(m => new { m.ProductId, m.Quantity }).ToListAsync())
            .GroupBy(m => m.ProductId).ToDictionary(g => g.Key, g => -g.Sum(m => m.Quantity));

        sb.AppendLine($"## Stok ({products.Count} ürün{(products.Count > MaxProducts ? $"; en çok satan {MaxProducts} tanesi listelendi" : "")})");
        sb.AppendLine("Sütunlar: ürün | stok / kapasite | kritik seviye | alış | satış | marj | son 30 gün satılan | stok kaç gün yeter | tedarikçi");
        foreach (var p in products.OrderByDescending(p => sold30.GetValueOrDefault(p.Id)).Take(MaxProducts).OrderBy(p => p.Name, StringComparer.Create(Tr, true)))
        {
            var sold = sold30.GetValueOrDefault(p.Id);
            var days = sold > 0 ? $"{Math.Floor(p.Stock / (sold / 30m)):0} gün" : "satış yok";
            var margin = p.Price > 0 ? $"%{Math.Round((p.Price - p.Cost) / p.Price * 100):0}" : "-";
            var flag = p.Stock <= p.MinStock ? " [KRİTİK]" : "";
            sb.AppendLine($"- {p.Name}{flag} | {Format.Qty(p.Stock)} / {Format.Qty(p.Capacity)} {p.Unit} | {Format.Qty(p.MinStock)} | " +
                          $"{Format.Money(p.Cost)} | {Format.Money(p.Price)} | {margin} | {Format.Qty(sold)} | {days} | " +
                          $"{(p.SupplierId is int sid && suppliers.TryGetValue(sid, out var sn) ? sn : "-")}");
        }
        sb.AppendLine();

        // ---- Borç / alacak ----
        var debts = (await db.Debts.AsNoTracking().Include(d => d.Payments).ToListAsync()).Where(d => d.Remaining > 0).ToList();
        string Party(Debt d) => d.SupplierId is not null ? d.Party
            : names.Alias(d.Party, d.Kind == DebtKind.Receivable ? "Müşteri" : "Kişi");
        string Due(Debt d) => d.DueDate is not { } due ? "vadesiz"
            : due < today ? $"vade {due.ToString("d MMM", Tr)}, {today.DayNumber - due.DayNumber} gün gecikmiş"
            : $"vade {due.ToString("d MMM", Tr)} ({due.DayNumber - today.DayNumber} gün kaldı)";

        foreach (var (kind, title) in new[] { (DebtKind.Payable, "Borçlarımız (ödenecek)"), (DebtKind.Receivable, "Alacaklarımız (tahsil edilecek)") })
        {
            var list = debts.Where(d => d.Kind == kind).OrderBy(d => d.DueDate ?? DateOnly.MaxValue).ToList();
            var overdue = list.Where(d => d.DueDate < today).ToList();
            sb.AppendLine($"## {title}: toplam {Format.Money(list.Sum(d => d.Remaining))}, {list.Count} kayıt; gecikmiş {Format.Money(overdue.Sum(d => d.Remaining))} ({overdue.Count} kayıt)");
            foreach (var d in list.Take(30))
                sb.AppendLine($"- {Party(d)}: kalan {Format.Money(d.Remaining)} / {Format.Money(d.Amount)}, {Due(d)}");
        }
        sb.AppendLine();

        // ---- Çalışanlar ----
        sb.AppendLine("## Kullanıcılar ve satışları");
        foreach (var u in (await users.ListAsync()).Where(u => u.IsActive || u.Month.Count > 0))
        {
            var alias = names.Alias(u.DisplayName, u.Role == Role.Owner ? "Sahip" : "Çalışan");
            sb.AppendLine($"- {alias} ({Format.Label(u.Role)}{(u.IsActive ? "" : ", devre dışı")}): bugün {u.Today.Count} satış {Format.Money(u.Today.Total)}, " +
                          $"bu ay {u.Month.Count} satış {Format.Money(u.Month.Total)}");
        }

        return new ShopSnapshot(sb.ToString(), names);
    }

    private static string Month(DateOnly d) => d.ToString("MMMM", Tr);

    private static string Totals(ReportDto r) =>
        $"gelir {Format.Money(r.Totals.Income)}, gider {Format.Money(r.Totals.Expense)}, net {Format.Money(r.Totals.Net)}";

    private static string SalesLine(ReportDto r) => r.Sales.Count == 0 ? "satış yok" :
        $"{r.Sales.Count} satış, ciro {Format.Money(r.Sales.Revenue)}, brüt kâr {Format.Money(r.Sales.GrossProfit)} (marj %{r.Sales.Margin.ToString("0.#", Tr)}), " +
        $"ortalama sepet {Format.Money(r.Sales.AvgBasket)}, indirim {Format.Money(r.Sales.Discount)}" +
        (r.Sales.Cancelled > 0 ? $", {r.Sales.Cancelled} iptal" : "");
}
