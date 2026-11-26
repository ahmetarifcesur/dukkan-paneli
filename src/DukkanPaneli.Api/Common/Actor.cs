using System.Security.Claims;
using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Common;

/// <summary>İşlemi yapan kullanıcı. Servisler HttpContext'i bilmez; kimin işlem yaptığını bu kayıtla alır.</summary>
public sealed record Actor(int? Id, string Name, Role Role)
{
    public bool IsOwner => Role == Role.Owner;

    /// <summary>Oturum çerezindeki bilgilerden (claim) Actor üretir. Çerezi AuthService.SignInAsync yazar.</summary>
    public static Actor From(ClaimsPrincipal user) => new(
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        user.FindFirstValue(Claims.DisplayName) ?? user.Identity?.Name ?? "?",
        Enum.Parse<Role>(user.FindFirstValue(ClaimTypes.Role)!));

    /// <summary>Çerezde kullandığımız özel claim adları.</summary>
    public static class Claims
    {
        public const string DisplayName = "display";
        public const string SecurityStamp = "stamp";
    }
}
