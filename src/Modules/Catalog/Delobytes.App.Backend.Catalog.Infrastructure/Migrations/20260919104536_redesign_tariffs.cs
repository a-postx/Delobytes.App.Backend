using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class redesign_tariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TariffGridEntries",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TariffGrids",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "LogisticsToCost",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.DropColumn(
                name: "PackingUnitId",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "TariffGridId",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "VolumeLiters",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.RenameColumn(
                name: "LogisticsToCustomerCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "ChannelCostTotal");

            migrationBuilder.CreateTable(
                name: "CostTypes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductChannelCosts",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChannelCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductChannelCosts_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "catalog",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductChannelCosts_CostTypes_CostTypeId",
                        column: x => x.CostTypeId,
                        principalSchema: "catalog",
                        principalTable: "CostTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductChannelCosts_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CostTypes_IsActive",
                schema: "catalog",
                table: "CostTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CostTypes_TenantId",
                schema: "catalog",
                table: "CostTypes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelCosts_ChannelId",
                schema: "catalog",
                table: "ProductChannelCosts",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelCosts_CostTypeId",
                schema: "catalog",
                table: "ProductChannelCosts",
                column: "CostTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelCosts_ProductId_ChannelId",
                schema: "catalog",
                table: "ProductChannelCosts",
                columns: new[] { "ProductId", "ChannelId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelCosts_ProductId_ChannelId_CostTypeId",
                schema: "catalog",
                table: "ProductChannelCosts",
                columns: new[] { "ProductId", "ChannelId", "CostTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelCosts_TenantId",
                schema: "catalog",
                table: "ProductChannelCosts",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductChannelCosts",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CostTypes",
                schema: "catalog");

            migrationBuilder.RenameColumn(
                name: "ChannelCostTotal",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "LogisticsToCustomerCost");

            migrationBuilder.AddColumn<decimal>(
                name: "LogisticsToCost",
                schema: "catalog",
                table: "ProductChannelInputs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PackingUnitId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TariffGridId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeLiters",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "TariffGrids",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TariffType = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false)
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RegionOrCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VolumeThresholdLiters = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_TariffGridEntries_TariffGridId_RegionOrCity_VolumeThreshold~",
                schema: "catalog",
                table: "TariffGridEntries",
                columns: new[] { "TariffGridId", "RegionOrCity", "VolumeThresholdLiters" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffGridEntries_TenantId",
                schema: "catalog",
                table: "TariffGridEntries",
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
        }
    }
}
