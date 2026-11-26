using DukkanPaneli.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DukkanPaneli.Tests.Unit;

/// <summary>Var olan bir veritabanının yeni sürüme güncellenmesi (veri taşıyan migration'lar).</summary>
public sealed class MigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDb _db;

    public MigrationTests()
    {
        _connection.Open();
        _db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseSqlite(_connection).Options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Eski_satislar_kasiyer_adina_gore_kullaniciya_baglanir()
    {
        // 1) Veritabanını ilk sürüme kur ve o zamanki biçimde (sadece ad ile) satış ekle.
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261126051506_InitialCreate");
        await _db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Id, UserName, DisplayName, PasswordHash, Role, IsActive, SecurityStamp, CreatedAt)
            VALUES (1, 'sahip', 'Ahmet', '', 'Owner', 1, 'x', '2026-01-01'), (2, 'kasa', 'Ayşe', '', 'Staff', 1, 'y', '2026-01-01');
            INSERT INTO Sales (Date, CreatedAt, CreatedBy, PaymentType, Customer, Gross, Discount, Total, VatTotal, CostTotal, Note, IsCancelled, CancelReason)
            VALUES ('2026-11-01', '2026-11-01', 'Ayşe', 'Cash', '', 0, 0, 0, 0, 0, '', 0, ''),
                   ('2026-11-01', '2026-11-01', 'Ahmet', 'Cash', '', 0, 0, 0, 0, 0, '', 0, ''),
                   ('2026-11-01', '2026-11-01', 'Silinmiş Biri', 'Cash', '', 0, 0, 0, 0, 0, '', 0, '');
            """);

        // 2) En son sürüme güncelle.
        await migrator.MigrateAsync();

        var byName = await _db.Sales.AsNoTracking().ToDictionaryAsync(s => s.CreatedBy, s => s.CreatedById);
        Assert.Equal(2, byName["Ayşe"]);
        Assert.Equal(1, byName["Ahmet"]);
        Assert.Null(byName["Silinmiş Biri"]); // eşleşen kullanıcı yok: boş kalır, satış korunur
    }
}
