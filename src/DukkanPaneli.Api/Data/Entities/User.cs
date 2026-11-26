namespace DukkanPaneli.Api.Data;

public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Şifre, rol ya da aktiflik değişince yenilenir. Oturum çerezindeki değerle eşleşmeyen
    /// oturumlar her istekte reddedilir (bkz. AuthSetup.ValidateSessionAsync).
    /// </summary>
    public string SecurityStamp { get; set; } = NewStamp();

    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public static string NewStamp() => Guid.NewGuid().ToString("N");
}
