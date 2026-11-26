namespace DukkanPaneli.Api.Common;

/// <summary>Kullanıcıdan gelen metinleri temizleme.</summary>
public static class Input
{
    /// <summary>Boşlukları kırpar, null'ı boş metne çevirir ve uzunluğu sınırlar.</summary>
    public static string Clean(string? text, int maxLength = 200)
    {
        var t = (text ?? "").Trim();
        return t.Length > maxLength ? t[..maxLength] : t;
    }

    /// <summary>Temizlenmiş metin boşsa varsayılanı döndürür.</summary>
    public static string CleanOr(string? text, int maxLength, string fallback) =>
        Clean(text, maxLength) is { Length: > 0 } t ? t : fallback;
}
