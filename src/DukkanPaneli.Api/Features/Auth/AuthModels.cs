using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Features.Auth;

// İstek gövdeleri (request). Alanlar nullable: eksik alanı servis doğrular ve Türkçe hata döndürür.
public sealed record SetupRequest(string? ShopName, string? DisplayName, string? UserName, string? Password, bool LoadDemo);
public sealed record LoginRequest(string? UserName, string? Password);
public sealed record ChangePasswordRequest(string? Current, string? Next);
public sealed record CreateUserRequest(string? UserName, string? DisplayName, Role Role, string? Password);
public sealed record UpdateUserRequest(string? DisplayName, Role Role, bool IsActive, string? NewPassword);

// Yanıtlar (DTO). Varlığı (entity) doğrudan döndürmüyoruz: PasswordHash gibi alanlar dışarı sızmasın.
public sealed record MeDto(int Id, string UserName, string DisplayName, Role Role)
{
    public static MeDto From(User u) => new(u.Id, u.UserName, u.DisplayName, u.Role);
}

public sealed record UserDto(int Id, string UserName, string DisplayName, Role Role, bool IsActive, DateTime CreatedAt);

public sealed record AuthStatusDto(bool NeedsSetup, string ShopName, MeDto? User);
