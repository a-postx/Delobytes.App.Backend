using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Stage7_Catalogs_Addition : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PackagingComponents",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Unit = table.Column<int>(type: "integer", nullable: false),
                PricePerUnit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackagingComponents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TariffGrids",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TariffType = table.Column<int>(type: "integer", nullable: false),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TariffGrids", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TariffGridEntries",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TariffGridId = table.Column<Guid>(type: "uuid", nullable: false),
                RegionOrCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                VolumeThresholdLiters = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TariffGridEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_TariffGridEntries_TariffGrids_TariffGridId",
                    column: x => x.TariffGridId,
                    principalSchema: "catalog",
                    principalTable: "TariffGrids",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WorkRates",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                DailyWage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                AssemblyRatePerDay = table.Column<int>(type: "integer", nullable: false),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkRates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "RawMaterialRates",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                CostPerUnit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
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
            name: "IX_PackagingComponents_IsActive",
            schema: "catalog",
            table: "PackagingComponents",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponents_TenantId",
            schema: "catalog",
            table: "PackagingComponents",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_TariffGrids_IsActive",
            schema: "catalog",
            table: "TariffGrids",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_TariffGrids_TariffType",
            schema: "catalog",
            table: "TariffGrids",
            column: "TariffType");

        migrationBuilder.CreateIndex(
            name: "IX_TariffGrids_TenantId",
            schema: "catalog",
            table: "TariffGrids",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_TariffGrids_ValidFrom",
            schema: "catalog",
            table: "TariffGrids",
            column: "ValidFrom");

        migrationBuilder.CreateIndex(
            name: "IX_TariffGridEntries_TenantId",
            schema: "catalog",
            table: "TariffGridEntries",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_TariffGridEntries_TariffGridId_RegionOrCity_VolumeThresholdLiters",
            schema: "catalog",
            table: "TariffGridEntries",
            columns: new[] { "TariffGridId", "RegionOrCity", "VolumeThresholdLiters" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkRates_IsActive",
            schema: "catalog",
            table: "WorkRates",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_WorkRates_TenantId",
            schema: "catalog",
            table: "WorkRates",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkRates_ValidFrom",
            schema: "catalog",
            table: "WorkRates",
            column: "ValidFrom");

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

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RawMaterialRates",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "TariffGridEntries",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "TariffGrids",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "WorkRates",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "PackagingComponents",
            schema: "catalog");
    }
}
