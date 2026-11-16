using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class fixed_bomline_deletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BomLines_ProductId_IsActive",
                schema: "catalog",
                table: "BomLines");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "catalog",
                table: "BomLines");

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidTo",
                schema: "catalog",
                table: "BomLines",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_ProductId_ValidTo",
                schema: "catalog",
                table: "BomLines",
                columns: new[] { "ProductId", "ValidTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BomLines_ProductId_ValidTo",
                schema: "catalog",
                table: "BomLines");

            migrationBuilder.DropColumn(
                name: "ValidTo",
                schema: "catalog",
                table: "BomLines");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "catalog",
                table: "BomLines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_ProductId_IsActive",
                schema: "catalog",
                table: "BomLines",
                columns: new[] { "ProductId", "IsActive" });
        }
    }
}
