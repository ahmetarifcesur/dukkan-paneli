using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DukkanPaneli.Api.Data.Migrations
{
    /// <summary>
    /// Sadece veri taşıyan migration: eski satışlarda kasiyerin yalnızca görünen adı var; aynı ada sahip
    /// kullanıcıyla eşleştirir. İki kullanıcının görünen adı aynıysa ilk eklenen seçilir; eşleşmeyen satış boş kalır.
    /// Tekrar çalışsa da zararsızdır (sadece boş olanları doldurur).
    /// </summary>
    public partial class BackfillSaleCreatedById : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE Sales
                SET CreatedById = (SELECT u.Id FROM Users u WHERE u.DisplayName = Sales.CreatedBy ORDER BY u.Id LIMIT 1)
                WHERE CreatedById IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alırken doldurulan numaraları silmeye gerek yok; sütun önceki migration'da kaldırılır.
        }
    }
}
