using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DukkanPaneli.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaleCreatedByIdAndLastLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedById",
                table: "Sales",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedById",
                table: "Sales",
                column: "CreatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Users_CreatedById",
                table: "Sales",
                column: "CreatedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Eski satışların doldurulması ayrı migration'da (BackfillSaleCreatedById): SQLite'ta yabancı anahtar
            // eklemek tabloyu yeniden kurar; veri güncellemesini bunun ardından ayrı yapmak daha güvenli.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Users_CreatedById",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_CreatedById",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Sales");
        }
    }
}
