using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Stage10_AddProductWorkRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AssemblyRatePerDay",
            schema: "catalog",
            table: "WorkRates");

        migrationBuilder.CreateTable(
            name: "ProductWorkRates",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                AssemblyRatePerDay = table.Column<int>(type: "integer", nullable: false),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductWorkRates", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductWorkRates_Products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

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

        migrationBuilder.CreateIndex(
            name: "IX_ProductWorkRates_TenantId",
            schema: "catalog",
            table: "ProductWorkRates",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProductWorkRates",
            schema: "catalog");

        migrationBuilder.AddColumn<int>(
            name: "AssemblyRatePerDay",
            schema: "catalog",
            table: "WorkRates",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }
}
