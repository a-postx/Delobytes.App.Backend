using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Stage11_PackagingComponentPrices : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PackagingComponentPrices",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackagingComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                PricePerUnit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackagingComponentPrices", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackagingComponentPrices_PackagingComponents_PackagingComp~",
                    column: x => x.PackagingComponentId,
                    principalSchema: "catalog",
                    principalTable: "PackagingComponents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_PackagingComponentPrices_Suppliers_SupplierId",
                    column: x => x.SupplierId,
                    principalSchema: "catalog",
                    principalTable: "Suppliers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        // Backfill one price version per existing component before the price columns are dropped.
        // ValidFrom is taken from the component creation date so already-recorded history keeps its ordering.
        migrationBuilder.Sql(@"
            INSERT INTO catalog.""PackagingComponentPrices""
                (""Id"", ""PackagingComponentId"", ""PricePerUnit"", ""SupplierId"", ""ValidFrom"", ""IsActive"", ""CreatedAt"", ""UpdatedAt"", ""TenantId"")
            SELECT
                gen_random_uuid(),
                pc.""Id"",
                pc.""PricePerUnit"",
                pc.""SupplierId"",
                pc.""CreatedAt""::date,
                pc.""IsActive"",
                pc.""CreatedAt"",
                NULL,
                pc.""TenantId""
            FROM catalog.""PackagingComponents"" AS pc;");

        migrationBuilder.DropColumn(name: "PricePerUnit", schema: "catalog", table: "PackagingComponents");
        migrationBuilder.DropColumn(name: "SupplierId", schema: "catalog", table: "PackagingComponents");
        migrationBuilder.DropColumn(name: "UpdatedAt", schema: "catalog", table: "PackagingComponents");

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponentPrices_IsActive",
            schema: "catalog",
            table: "PackagingComponentPrices",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponentPrices_PackagingComponentId",
            schema: "catalog",
            table: "PackagingComponentPrices",
            column: "PackagingComponentId");

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponentPrices_TenantId_ComponentId_ValidFrom",
            schema: "catalog",
            table: "PackagingComponentPrices",
            columns: new[] { "TenantId", "PackagingComponentId", "ValidFrom" });

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponentPrices_SupplierId",
            schema: "catalog",
            table: "PackagingComponentPrices",
            column: "SupplierId");

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponentPrices_TenantId",
            schema: "catalog",
            table: "PackagingComponentPrices",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "PricePerUnit",
            schema: "catalog",
            table: "PackagingComponents",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<Guid>(
            name: "SupplierId",
            schema: "catalog",
            table: "PackagingComponents",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            schema: "catalog",
            table: "PackagingComponents",
            type: "timestamp with time zone",
            nullable: true);

        // Restore the latest price version of each component back onto the component row.
        migrationBuilder.Sql(@"
            UPDATE catalog.""PackagingComponents"" AS pc
            SET ""PricePerUnit"" = latest.""PricePerUnit"",
                ""SupplierId"" = latest.""SupplierId"",
                ""UpdatedAt"" = latest.""UpdatedAt""
            FROM (
                SELECT DISTINCT ON (""PackagingComponentId"")
                    ""PackagingComponentId"", ""PricePerUnit"", ""SupplierId"", ""UpdatedAt""
                FROM catalog.""PackagingComponentPrices""
                ORDER BY ""PackagingComponentId"", ""ValidFrom"" DESC, ""CreatedAt"" DESC
            ) AS latest
            WHERE pc.""Id"" = latest.""PackagingComponentId"";");

        migrationBuilder.DropTable(
            name: "PackagingComponentPrices",
            schema: "catalog");
    }
}
