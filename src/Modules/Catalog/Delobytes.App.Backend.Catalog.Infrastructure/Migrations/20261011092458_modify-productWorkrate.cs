using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class modifyproductWorkrate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductWorkRates_IsActive",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropIndex(
                name: "IX_ProductWorkRates_ProductId_ValidFrom",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RemovedFrom",
                schema: "catalog",
                table: "ProductWorkRates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "catalog",
                table: "ProductWorkRates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_ProductId",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_Status",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_TenantId_ProductId_ValidFrom",
                schema: "catalog",
                table: "ProductWorkRates",
                columns: new[] { "TenantId", "ProductId", "ValidFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductWorkRates_ProductId",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropIndex(
                name: "IX_ProductWorkRates_Status",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropIndex(
                name: "IX_ProductWorkRates_TenantId_ProductId_ValidFrom",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropColumn(
                name: "RemovedFrom",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "catalog",
                table: "ProductWorkRates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_IsActive",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_ProductId_ValidFrom",
                schema: "catalog",
                table: "ProductWorkRates",
                columns: new[] { "ProductId", "ValidFrom" });
        }
    }
}
