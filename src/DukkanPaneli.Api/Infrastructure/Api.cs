using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Infrastructure;

public static class ApiKit
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static IResult Fail(string message, int status = 400, object? extra = null) =>
        Results.Json(new { error = message, details = extra }, statusCode: status);

    /// <summary>Başarılı değişiklik yanıtı: istemci "Geri al" düğmesi için auditId'yi kullanır.</summary>
    public static IResult Done(AuditLog log, object? data = null) =>
        Results.Ok(new { auditId = log.Id, undoable = log.Undoable, summary = log.Summary, data });

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public static decimal R2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    public static decimal R4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
    public static string Money(decimal v) => v.ToString("#,##0.##", Tr) + " ₺";
    public static string Qty(decimal v) => v.ToString("#,##0.###", Tr);
    public static string SaleNo(int id) => $"F-{id:D6}";
    public static string Clean(string? s, int max = 200)
    {
        var t = (s ?? "").Trim();
        return t.Length > max ? t[..max] : t;
    }
    /// <summary>KDV dahil tutardan KDV payı.</summary>
    public static decimal VatOf(decimal grossAmount, decimal rate) => rate <= 0 ? 0 : R2(grossAmount * rate / (100 + rate));

    public static Actor ActorOf(ClaimsPrincipal u) => new(
        int.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!),
        u.FindFirstValue("display") ?? u.Identity?.Name ?? "?",
        Enum.Parse<Role>(u.FindFirstValue(ClaimTypes.Role)!));

    public static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var t = Today;
        var f = from ?? new DateOnly(t.Year, t.Month, 1);
        var e = to ?? f.AddMonths(1).AddDays(-1);
        return f <= e ? (f, e) : (e, f);
    }
}

/// <summary>decimal değerleri gereksiz sıfırlar olmadan yazar (12.5000 yerine 12.5).</summary>
public sealed class CleanDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture)
            : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.####", CultureInfo.InvariantCulture));
}
