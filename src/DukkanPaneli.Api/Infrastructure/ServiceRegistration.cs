using System.Text.Json.Serialization;
using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Features.Admin;
using DukkanPaneli.Api.Features.Auth;
using DukkanPaneli.Api.Features.Calendar;
using DukkanPaneli.Api.Features.Catalog;
using DukkanPaneli.Api.Features.Dashboard;
using DukkanPaneli.Api.Features.Finance;
using DukkanPaneli.Api.Features.History;
using DukkanPaneli.Api.Features.Reports;
using DukkanPaneli.Api.Features.Sales;
using Microsoft.EntityFrameworkCore;

namespace DukkanPaneli.Api.Infrastructure;

/// <summary>Bağımlılık enjeksiyonu (DI) kayıtları ve uç noktaların bağlanması.</summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Veritabanı: varsayılan App_Data/dukkan.db. Testler "ConnectionStrings:Default" ayarıyla
    /// geçici bir dosya verir (bkz. tests/…/TestApp.cs).
    /// </summary>
    public static IServiceCollection AddDukkanDatabase(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        var connectionString = config.GetConnectionString("Default") ?? $"Data Source={Path.Combine(dataDir, "dukkan.db")}";
        return services.AddDbContext<AppDb>(o => o.UseSqlite(connectionString));
    }

    /// <summary>
    /// Servisler "scoped" kaydedilir: her HTTP isteği kendi örneğini (ve kendi AppDb'sini) alır.
    /// Uç noktalar servisleri parametre olarak ister, ASP.NET Core bunları otomatik verir.
    /// </summary>
    public static IServiceCollection AddDukkanServices(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.Converters.Add(new CleanDecimalConverter());
        });

        services.AddSingleton<SetupGuard>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<ProductService>();
        services.AddScoped<StockService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<SaleService>();
        services.AddScoped<CashService>();
        services.AddScoped<DebtService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<HistoryService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AdminService>();
        services.AddScoped<DemoDataService>();
        return services;
    }

    public static void MapDukkanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints();
        app.MapDashboardEndpoints();
        app.MapCatalogEndpoints();
        app.MapSalesEndpoints();
        app.MapFinanceEndpoints();
        app.MapCalendarEndpoints();
        app.MapHistoryEndpoints();
        app.MapReportEndpoints();
        app.MapAdminEndpoints();
        app.MapFallback("/api/{**rest}", () => HttpResults.Fail("Bulunamadı.", StatusCodes.Status404NotFound));
    }
}
