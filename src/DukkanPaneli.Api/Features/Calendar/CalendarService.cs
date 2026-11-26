using System.Text.RegularExpressions;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Calendar;

/// <param name="Repeat">0 veya 1: tek seferlik. 2-24: her ay aynı gün, o kadar ay.</param>
public sealed record EventRequest(string? Title, DateOnly? Date, string? Time, string? Type, string? Note, int Repeat = 0);

public sealed partial class CalendarService(AppDb db)
{
    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex TimePattern();

    public Task<List<CalendarEvent>> ListAsync() => db.Events.AsNoTracking().OrderBy(e => e.Date).ToListAsync();

    public async Task<OpResult> CreateAsync(Actor actor, EventRequest r)
    {
        if (Validate(r) is { } error) return OpResult.Fail(error);
        var title = Input.Clean(r.Title, 80);
        var times = Math.Max(1, r.Repeat);
        var first = r.Date!.Value;
        for (var i = 0; i < times; i++)
        {
            // AddMonths ay sonunu korur: 31 Ocak + 1 ay → 28/29 Şubat.
            db.Events.Add(new CalendarEvent
            {
                Title = title, Date = first.AddMonths(i), Time = r.Time ?? "", Type = NormalizeType(r.Type), Note = Input.Clean(r.Note, 200),
            });
        }
        var log = await db.SaveAuditedAsync(actor, "event.create",
            () => $"Etkinlik eklendi: {title} ({first:dd.MM.yyyy}{(times > 1 ? $", {times} ay tekrar" : "")})");
        return OpResult.Ok(log);
    }

    public async Task<OpResult> UpdateAsync(Actor actor, int id, EventRequest r)
    {
        var e = await db.Events.FindAsync(id);
        if (e is null) return OpResult.NotFound("Etkinlik bulunamadı.");
        if (Validate(r) is { } error) return OpResult.Fail(error);
        e.Title = Input.Clean(r.Title, 80);
        e.Date = r.Date!.Value;
        e.Time = r.Time ?? "";
        e.Type = NormalizeType(r.Type);
        e.Note = Input.Clean(r.Note, 200);
        var log = await db.SaveAuditedAsync(actor, "event.update", () => $"Etkinlik güncellendi: {e.Title} ({e.Date:dd.MM.yyyy})");
        return OpResult.Ok(log);
    }

    public async Task<OpResult> DeleteAsync(Actor actor, int id)
    {
        var e = await db.Events.FindAsync(id);
        if (e is null) return OpResult.NotFound("Etkinlik bulunamadı.");
        db.Events.Remove(e);
        var log = await db.SaveAuditedAsync(actor, "event.delete", () => $"Etkinlik silindi: {e.Title} ({e.Date:dd.MM.yyyy})");
        return OpResult.Ok(log);
    }

    private static string? Validate(EventRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) return "Başlık gerekli.";
        if (r.Date is null) return "Tarih seçin.";
        if (!string.IsNullOrEmpty(r.Time) && !TimePattern().IsMatch(r.Time)) return "Saat SS:dd biçiminde olmalı.";
        if (r.Repeat is < 0 or > 24) return "Tekrar 0-24 ay arasında olmalı.";
        return null;
    }

    private static string NormalizeType(string? type) => CalendarEvent.Types.Contains(type ?? "") ? type! : "diger";
}
