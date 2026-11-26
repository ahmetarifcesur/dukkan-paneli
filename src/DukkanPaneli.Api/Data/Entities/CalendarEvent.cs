namespace DukkanPaneli.Api.Data;

public class CalendarEvent
{
    /// <summary>Geçerli etkinlik türleri (arayüzdeki renkler bu kodlara göre seçilir).</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string> { "odeme", "teslimat", "toplanti", "kampanya", "diger" };

    public int Id { get; set; }
    public string Title { get; set; } = "";
    public DateOnly Date { get; set; }

    /// <summary>"SS:dd" biçiminde saat ya da boş.</summary>
    public string Time { get; set; } = "";

    public string Type { get; set; } = "diger";
    public string Note { get; set; } = "";
}
