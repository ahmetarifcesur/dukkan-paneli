using DukkanPaneli.Api.Common;
using DukkanPaneli.Api.Data;
using DukkanPaneli.Api.Infrastructure;

namespace DukkanPaneli.Api.Features.Reports;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/reports").RequireAuthorization(AuthSetup.OwnerPolicy);

        reports.MapGet("/", async (DateOnly? from, DateOnly? to, ReportService svc) =>
            Results.Ok(await svc.BuildAsync(DateRange.Of(from, to))));

        reports.MapGet("/excel", async (DateOnly? from, DateOnly? to, ReportService svc, AppDb db) =>
        {
            var range = DateRange.Of(from, to);
            var data = await svc.LoadAsync(range);
            var shop = (await db.ReadSettingsAsync()).GetValueOrDefault(Setting.ShopName, "Dükkan");
            return Results.File(ExcelExporter.Export(data, shop),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ExcelExporter.FileName(range));
        });
    }
}
