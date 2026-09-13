using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DropRawMaterialRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RawMaterialRates",
            schema: "catalog");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RawMaterialRates",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                CostPerUnit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RawMaterialRates", x => x.Id);
                table.ForeignKey(
                    name: "FK_RawMaterialRates_Products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RawMaterialRates_IsActive",
            schema: "catalog",
            table: "RawMaterialRates",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_RawMaterialRates_ProductId_ValidFrom",
            schema: "catalog",
            table: "RawMaterialRates",
            columns: new[] { "ProductId", "ValidFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_RawMaterialRates_TenantId",
            schema: "catalog",
            table: "RawMaterialRates",
            column: "TenantId");
    }
}
