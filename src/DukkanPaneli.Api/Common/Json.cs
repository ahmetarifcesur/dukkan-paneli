using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DukkanPaneli.Api.Common;

/// <summary>decimal değerleri gereksiz sıfırlar olmadan yazar (12.5000 yerine 12.5); "12,5" metnini de okur.</summary>
public sealed class CleanDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? decimal.Parse(reader.GetString()!.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture)
            : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.####", CultureInfo.InvariantCulture));
}

/// <summary>
/// Enum'ların JSON adları (<see cref="JsonStringEnumMemberNameAttribute"/>) ile çalışır.
/// Sorgu parametreleri (ör. <c>?type=Satis</c>) JSON'dan geçmediği için ayrıca çözmek gerekir.
/// </summary>
public static class EnumWire
{
    public static bool TryParse<T>(string? text, out T value) where T : struct, Enum
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var wire = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name;
            if (string.Equals(wire, text, StringComparison.OrdinalIgnoreCase) || string.Equals(field.Name, text, StringComparison.OrdinalIgnoreCase))
            {
                value = (T)field.GetValue(null)!;
                return true;
            }
        }
        value = default;
        return false;
    }
}
