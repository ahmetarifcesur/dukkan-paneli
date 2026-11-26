using System.Text.RegularExpressions;

namespace DukkanPaneli.Api.Features.Assistant;

/// <summary>
/// Kişisel verileri (müşteri ve çalışan adları) dışarı göndermeden önce takma adla değiştirir:
/// "Mehmet Bey" → "Müşteri-1". Modelin cevabı geldikten sonra takma adlar gerçek adlarla geri değiştirilir;
/// böylece dükkan sahibi gerçek adları görür ama adlar Anthropic'e hiç gitmez (KVKK: veri en aza indirme).
/// </summary>
public sealed class Pseudonyms
{
    private readonly Dictionary<string, string> _aliasByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameByAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

    /// <summary>Gerçek ada bir takma ad verir; aynı ad her seferinde aynı takma adı alır.</summary>
    public string Alias(string realName, string prefix)
    {
        var name = realName.Trim();
        if (name.Length == 0) return prefix;
        if (_aliasByName.TryGetValue(name, out var existing)) return existing;
        var n = _counters[prefix] = _counters.GetValueOrDefault(prefix) + 1;
        var alias = $"{prefix}-{n}";
        _aliasByName[name] = alias;
        _nameByAlias[alias] = name;
        return alias;
    }

    public int Count => _aliasByName.Count;

    /// <summary>Metindeki gerçek adları takma adlarla değiştirir (önceki cevaplar modele geri gönderilmeden önce).</summary>
    public string Hide(string text) => Replace(text, _aliasByName);

    /// <summary>Modelin cevabındaki takma adları gerçek adlarla değiştirir.</summary>
    public string Reveal(string text) => Replace(text, _nameByAlias);

    private static string Replace(string text, Dictionary<string, string> map)
    {
        if (map.Count == 0 || string.IsNullOrEmpty(text)) return text;
        // Uzun adlar önce: "Ali Veli" içindeki "Ali" yanlışlıkla değişmesin. Kelime sınırı Türkçe harfleri de kapsar;
        // "Ali" adı "Alişan" kelimesinin içinde değiştirilmez. "Müşteri-1", "Müşteri-12"nin başında eşleşmez.
        var pattern = string.Join("|", map.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        return Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}])(?:{pattern})(?![\p{{L}}\p{{N}}])", m => map[m.Value]);
    }
}
