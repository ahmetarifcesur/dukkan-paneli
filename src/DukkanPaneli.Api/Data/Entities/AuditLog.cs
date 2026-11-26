namespace DukkanPaneli.Api.Data;

/// <summary>
/// İşlem geçmişinin bir satırı: kim, ne zaman, ne yaptı. <see cref="Changes"/> etkilenen her kaydın
/// önceki ve sonraki hâlini JSON olarak tutar; geri alma bu bilgiyle yapılır (bkz. AppDb.Audit.cs).
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";

    /// <summary>Makinenin okuyacağı işlem adı, ör. "sale.create".</summary>
    public string Action { get; set; } = "";

    /// <summary>İnsanın okuyacağı özet, ör. "Satış F-000012: 2 kalem, 450 ₺".</summary>
    public string Summary { get; set; } = "";

    public string Changes { get; set; } = "[]";
    public bool Undoable { get; set; } = true;
    public DateTime? UndoneAt { get; set; }
    public int? UndoneByAuditId { get; set; }
    public int? UndoOfAuditId { get; set; }
}
