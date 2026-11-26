namespace DukkanPaneli.Api.Data;

/// <summary>Anahtar/değer ayarları (dükkan adı, varsayılan KDV oranı).</summary>
public class Setting
{
    public const string ShopName = "shopName";
    public const string DefaultVatRate = "defaultVatRate";

    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
