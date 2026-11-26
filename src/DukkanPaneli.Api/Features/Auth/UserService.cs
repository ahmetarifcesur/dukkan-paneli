using System.Security.Cryptography;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Features.Auth;

/// <summary>Kullanıcı yönetimi (yalnızca sahip). Kullanıcı işlemleri geçmişe yazılır ama geri alınamaz.</summary>
public sealed class UserService(AppDb db, IPasswordHasher<User> hasher)
{
    /// <summary>Kullanıcılar ve her birinin bugünkü / bu ayki satışı (iptal edilenler hariç), son satışı ve son girişi.</summary>
    public async Task<List<UserDto>> ListAsync()
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();

        var today = Clock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        // Para alanları veritabanında ölçeklenmiş tamsayı olarak duruyor (ScaledDecimalConverter); SQL'de toplamak
        // yerine bu ayın satışlarını belleğe alıp gruplamak hem basit hem yeterince hızlı (tek dükkan).
        var monthSales = await db.Sales.AsNoTracking()
            .Where(s => !s.IsCancelled && s.CreatedById != null && s.Date >= monthStart && s.Date <= today)
            .Select(s => new { s.CreatedById, s.Date, s.Total })
            .ToListAsync();

        // Her kullanıcının en son satışı: Id'ler artan olduğu için en büyük Id en son satıştır.
        var lastSaleIds = await db.Sales.AsNoTracking()
            .Where(s => s.CreatedById != null)
            .GroupBy(s => s.CreatedById)
            .Select(g => g.Max(s => s.Id))
            .ToListAsync();
        var lastSaleAt = await db.Sales.AsNoTracking()
            .Where(s => lastSaleIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.CreatedById!.Value, s => s.CreatedAt);

        SalesStatsDto Stats(int userId, DateOnly from)
        {
            var list = monthSales.Where(s => s.CreatedById == userId && s.Date >= from).ToList();
            return new SalesStatsDto(list.Count, list.Sum(s => s.Total));
        }

        return users.Select(u => new UserDto(u.Id, u.UserName, u.DisplayName, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt,
                Stats(u.Id, today), Stats(u.Id, monthStart), lastSaleAt.TryGetValue(u.Id, out var at) ? at : null))
            .ToList();
    }

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

    /// <summary>
    /// Unutulan şifre için: rastgele geçici bir şifre üretir, hesabı etkinleştirir ve açık oturumları kapatır.
    /// Web'den çağrılamaz; sadece sunucuda çalıştırılan komutla kullanılır (bkz. Infrastructure/Commands.cs).
    /// </summary>
    /// <returns>Geçici şifre.</returns>
    public async Task<Result<string>> ResetPasswordAsync(Actor actor, string? userName)
    {
        var name = AuthService.NormalizeUserName(userName);
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == name);
        if (user is null) return Result<string>.Fail($"Kullanıcı bulunamadı: {name}", StatusCodes.Status404NotFound);

        // Karıştırılabilecek karakterler (0/O, 1/l/I) yok; telefondan okuyup yazması kolay olsun.
        var temp = RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyz23456789", 12);
        user.PasswordHash = hasher.HashPassword(user, temp);
        user.SecurityStamp = User.NewStamp();
        user.IsActive = true;
        await db.SaveAuditedAsync(actor, "user.password-reset", () => $"Şifre sıfırlandı (sunucu komutu): {user.UserName}", undoable: false);
        return Result<string>.Ok(temp);
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
