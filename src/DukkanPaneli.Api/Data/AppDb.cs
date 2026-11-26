using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Data;

/// <summary>
/// EF Core veritabanı bağlamı: tablolar (DbSet) ve aralarındaki ilişkiler burada tanımlanır.
/// Denetim kaydı ve geri alma aynı sınıfın diğer parçasındadır: <c>AppDb.Audit.cs</c>.
/// Şema değişince yeni migration üretilir: <c>dotnet ef migrations add Ad -o Data/Migrations</c>.
/// </summary>
public partial class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<CashEntry> CashEntries => Set<CashEntry>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<DebtPayment> DebtPayments => Set<DebtPayment>();
    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Tüm modele uygulanan kurallar: belirli bir türdeki her sütun nasıl saklanacak.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HaveConversion<ScaledDecimalConverter>();
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enum'lar sayı yerine ad olarak saklanır: veritabanını açınca "Income" okumak 0'dan anlaşılırdır
        // ve enum'a yeni değer eklemek eski kayıtları bozmaz.
        b.Properties<Role>().HaveConversion<string>();
        b.Properties<CashType>().HaveConversion<string>();
        b.Properties<PaymentType>().HaveConversion<string>();
        b.Properties<DebtKind>().HaveConversion<string>();
        b.Properties<MovementType>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        // Hiçbir ilişkide zincirleme silme (cascade) yok: bir kaydı silmek, ona bağlı kayıtları
        // sessizce silmemeli. Silme gerekiyorsa servis bunu açıkça yapar (ve geçmişe yazılır).
        const DeleteBehavior Restrict = DeleteBehavior.Restrict;

        m.Entity<User>().HasIndex(x => x.UserName).IsUnique();
        m.Entity<Setting>().HasKey(x => x.Key);

        m.Entity<Product>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(Restrict);

        m.Entity<Sale>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(Restrict);
        m.Entity<Sale>().HasIndex(x => x.Date);
        m.Entity<SaleLine>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(Restrict);

        m.Entity<StockMovement>().HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(Restrict);
        m.Entity<StockMovement>().HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(Restrict);
        m.Entity<StockMovement>().HasIndex(x => new { x.ProductId, x.Date });

        m.Entity<CashEntry>().HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(Restrict);
        m.Entity<CashEntry>().HasOne(x => x.DebtPayment).WithMany().HasForeignKey(x => x.DebtPaymentId).OnDelete(Restrict);
        m.Entity<CashEntry>().HasOne(x => x.StockMovement).WithMany().HasForeignKey(x => x.StockMovementId).OnDelete(Restrict);
        m.Entity<CashEntry>().HasIndex(x => x.Date);

        m.Entity<Debt>().HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.DebtId).OnDelete(Restrict);
        m.Entity<Debt>().HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(Restrict);
        m.Entity<Debt>().HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(Restrict);
        m.Entity<Debt>().HasOne(x => x.StockMovement).WithMany().HasForeignKey(x => x.StockMovementId).OnDelete(Restrict);

        m.Entity<CalendarEvent>().HasIndex(x => x.Date);
        m.Entity<AuditLog>().HasIndex(x => x.At);
    }

    /// <summary>Ayarları sözlük olarak okur.</summary>
    public Task<Dictionary<string, string>> ReadSettingsAsync(CancellationToken ct = default) =>
        Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
}
