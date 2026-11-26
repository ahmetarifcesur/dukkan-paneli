using System.Text.Json;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.History;

public sealed record HistoryItemDto(int Id, DateTime At, string UserName, string Action, string Summary, bool Undoable,
    DateTime? UndoneAt, int? UndoneByAuditId, int? UndoOfAuditId, int ChangeCount);

public sealed record HistoryPageDto(int Total, int Page, int PageSize, List<string> Users, IEnumerable<HistoryItemDto> Items);

public sealed record FieldChangeDto(string Field, string? Before, string? After);

public sealed record RecordChangeDto(string Entity, string Label, string? Key, string State, IEnumerable<FieldChangeDto> Fields);

public sealed record HistoryDetailDto(int Id, DateTime At, string UserName, string Action, string Summary, bool Undoable,
    DateTime? UndoneAt, int? UndoneByAuditId, int? UndoOfAuditId, IEnumerable<RecordChangeDto> Changes);

/// <summary>İşlem geçmişini listeleme, ayrıntı ve geri alma. Asıl mekanizma: AppDb.Audit.cs.</summary>
public sealed class HistoryService(AppDb db)
{
    public const int PageSize = 50;

    public async Task<HistoryPageDto> ListAsync(DateOnly? from, DateOnly? to, string? user, string? search, int? page)
    {
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        // Tarihler yerel gün; At ise UTC saklanır. Günü UTC aralığına çeviriyoruz.
        if (from is { } f)
        {
            var start = f.ToDateTime(TimeOnly.MinValue).ToUniversalTime();
            q = q.Where(a => a.At >= start);
        }
        if (to is { } t)
        {
            var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue).ToUniversalTime();
            q = q.Where(a => a.At < end);
        }
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.UserName == user);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.Summary.ToLower().Contains(search.ToLower()));

        var total = await q.CountAsync();
        var pageNo = Math.Max(1, page ?? 1);
        var items = await q.OrderByDescending(a => a.Id).Skip((pageNo - 1) * PageSize).Take(PageSize).ToListAsync();
        var users = await db.AuditLogs.AsNoTracking().Select(a => a.UserName).Distinct().ToListAsync();

        return new HistoryPageDto(total, pageNo, PageSize, users, items.Select(a => new HistoryItemDto(
            a.Id, a.At, a.UserName, a.Action, a.Summary, a.Undoable, a.UndoneAt, a.UndoneByAuditId, a.UndoOfAuditId,
            AppDb.ParseChanges(a.Changes).Count)));
    }

    /// <summary>Hangi kayıtta hangi alan neyden neye değişti.</summary>
    public async Task<HistoryDetailDto?> DetailAsync(int id)
    {
        var a = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return null;

        var changes = AppDb.ParseChanges(a.Changes).Select(c =>
        {
            var values = (c.After ?? c.Before)!;
            var fields = values.Keys
                // Değişiklikte sadece farklı alanları göster; ekleme/silmede tüm alanları (CreatedAt hariç).
                .Where(k => c.State != "Modified" || c.Before![k].GetRawText() != c.After![k].GetRawText())
                .Where(k => c.State == "Modified" || k != "CreatedAt")
                .Select(k => new FieldChangeDto(k, c.Before is null ? null : Show(c.Before[k]), c.After is null ? null : Show(c.After[k])));
            var key = values.TryGetValue("Id", out var idEl) ? Show(idEl) : values.TryGetValue("Key", out var keyEl) ? Show(keyEl) : "";
            return new RecordChangeDto(c.Entity.Split('.').Last(), AppDb.EntityLabel(c.Entity), key, c.State, fields);
        });

        return new HistoryDetailDto(a.Id, a.At, a.UserName, a.Action, a.Summary, a.Undoable, a.UndoneAt, a.UndoneByAuditId, a.UndoOfAuditId, changes);
    }

    public async Task<OpResult> UndoAsync(Actor actor, int id)
    {
        var r = await db.UndoAsync(id, actor);
        return r.Error is null ? OpResult.Ok(r.Log!) : OpResult.Fail(r.Error, StatusCodes.Status409Conflict, r.Blockers);
    }

    private static string? Show(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => "evet",
        JsonValueKind.False => "hayır",
        _ => e.GetRawText(),
    };
}
