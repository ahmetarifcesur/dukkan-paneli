using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Auth;

/// <summary>Kullanıcı yönetimi (yalnızca sahip). Kullanıcı işlemleri geçmişe yazılır ama geri alınamaz.</summary>
public sealed class UserService(AppDb db, IPasswordHasher<User> hasher)
{
    public Task<List<UserDto>> ListAsync() =>
        db.Users.OrderBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.CreatedAt))
            .ToListAsync();

    public async Task<OpResult> CreateAsync(Actor actor, CreateUserRequest req)
    {
        var userName = AuthService.NormalizeUserName(req.UserName);
        if (AuthService.ValidateUserName(userName) is { } e1) return OpResult.Fail(e1);
        if (AuthService.ValidatePassword(req.Password) is { } e2) return OpResult.Fail(e2);
        if (await db.Users.AnyAsync(x => x.UserName == userName)) return OpResult.Fail("Bu kullanıcı adı zaten kullanılıyor.");

        var user = new User
        {
            UserName = userName,
            DisplayName = Input.CleanOr(req.DisplayName, 60, userName),
            Role = req.Role,
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password!);
        db.Users.Add(user);

        var log = await db.SaveAuditedAsync(actor, "user.create",
            () => $"Kullanıcı eklendi: {user.DisplayName} ({user.UserName}, {Format.Label(user.Role)})", undoable: false);
        return OpResult.Ok(log, new { user.Id });
    }

    public async Task<OpResult> UpdateAsync(Actor actor, int id, UpdateUserRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return OpResult.NotFound("Kullanıcı bulunamadı.");

        // Kendini kilitleme koruması: dükkanda en az bir aktif sahip kalmalı.
        var losesOwner = user.Role == Role.Owner && user.IsActive && (req.Role != Role.Owner || !req.IsActive);
        if (losesOwner && await db.Users.CountAsync(x => x.Role == Role.Owner && x.IsActive) <= 1)
            return OpResult.Fail("En az bir aktif sahip hesabı kalmalı.");

        var changes = new List<string>();
        if (user.Role != req.Role) changes.Add($"rol {Format.Label(user.Role)} → {Format.Label(req.Role)}");
        if (user.IsActive != req.IsActive) changes.Add(req.IsActive ? "etkinleştirildi" : "devre dışı bırakıldı");

        var display = Input.Clean(req.DisplayName, 60);
        if (display.Length > 0 && display != user.DisplayName)
        {
            changes.Add($"ad {user.DisplayName} → {display}");
            user.DisplayName = display;
        }

        var resetPassword = !string.IsNullOrEmpty(req.NewPassword);
        if (resetPassword)
        {
            if (AuthService.ValidatePassword(req.NewPassword) is { } e) return OpResult.Fail(e);
            user.PasswordHash = hasher.HashPassword(user, req.NewPassword!);
            changes.Add("şifre sıfırlandı");
        }

        // Yetkiyi etkileyen bir değişiklik varsa kullanıcının açık oturumlarını kapat.
        if (user.Role != req.Role || user.IsActive != req.IsActive || resetPassword)
            user.SecurityStamp = User.NewStamp();
        user.Role = req.Role;
        user.IsActive = req.IsActive;

        var log = await db.SaveAuditedAsync(actor, "user.update",
            () => $"Kullanıcı güncellendi: {user.UserName}" + (changes.Count > 0 ? $" ({string.Join(", ", changes)})" : ""),
            undoable: false);
        return OpResult.Ok(log);
    }
}
