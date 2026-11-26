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

    private static readonly System.Globalization.CompareInfo Turkish = new System.Globalization.CultureInfo("tr-TR").CompareInfo;

    /// <summary>
    /// Büyük/küçük harf farkı gözetmeden, Türkçe kurallarıyla karşılaştırır ("ÇAY" = "çay", "IŞIK" = "ışık").
    /// SQLite'ın lower() fonksiyonu sadece İngilizce harfleri küçültür; bu yüzden isim çakışmalarını
    /// veritabanında değil burada kontrol ediyoruz.
    /// </summary>
    public static bool SameText(string a, string b) =>
        Turkish.Compare(a.Trim(), b.Trim(), System.Globalization.CompareOptions.IgnoreCase) == 0;

    /// <summary>Temizlenmiş metin boşsa varsayılanı döndürür.</summary>
    public static string CleanOr(string? text, int maxLength, string fallback) =>
        Clean(text, maxLength) is { Length: > 0 } t ? t : fallback;
}
