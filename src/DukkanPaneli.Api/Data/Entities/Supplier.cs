namespace DukkanPaneli.Api.Data;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>Silmek yerine arşivlenir; eski borçlar ve ürünler bozulmaz.</summary>
    public bool IsArchived { get; set; }
}
