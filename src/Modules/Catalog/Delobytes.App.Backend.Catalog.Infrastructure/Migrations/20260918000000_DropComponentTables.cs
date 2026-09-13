using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DropComponentTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProductComponents",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Components",
            schema: "catalog");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Components",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
                Unit = table.Column<int>(type: "integer", nullable: false),
                PurchasePrice = table.Column<decimal>(type: "numeric", nullable: false),
                Supplier = table.Column<string>(type: "text", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Components", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProductComponents",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductComponents", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductComponents_Components_ComponentId",
                    column: x => x.ComponentId,
                    principalSchema: "catalog",
                    principalTable: "Components",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProductComponents_Products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Components_TenantId",
            schema: "catalog",
            table: "Components",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductComponents_ComponentId",
            schema: "catalog",
            table: "ProductComponents",
            column: "ComponentId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductComponents_ProductId_ComponentId",
            schema: "catalog",
            table: "ProductComponents",
            columns: new[] { "ProductId", "ComponentId" });

        migrationBuilder.CreateIndex(
            name: "IX_ProductComponents_TenantId",
            schema: "catalog",
            table: "ProductComponents",
            column: "TenantId");
    }
}
