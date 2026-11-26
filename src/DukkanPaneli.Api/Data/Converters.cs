using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DukkanPaneli.Api.Data;

/// <summary>
/// SQLite'ta decimal yerel bir tür değildir (EF Core onu metin olarak saklar; SQL'de toplama/sıralama yanlış çalışır).
/// Bu yüzden 4 basamak hassasiyetle tamsayıya çevirip saklıyoruz: 12,5 → 125000.
/// </summary>
public sealed class ScaledDecimalConverter() : ValueConverter<decimal, long>(
    v => (long)Math.Round(v * 10000m),
    v => v / 10000m);

/// <summary>
/// Zaman damgaları UTC saklanır. SQLite tarih türünü (Kind) saklamadığı için okurken UTC olarak işaretliyoruz;
/// aksi hâlde JSON'a "Z" eki olmadan yazılır ve tarayıcı yerel saat sanar (Türkiye'de 3 saat kayma).
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
