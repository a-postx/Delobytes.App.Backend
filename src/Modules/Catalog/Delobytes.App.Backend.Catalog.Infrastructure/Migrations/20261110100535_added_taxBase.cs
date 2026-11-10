using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_taxBase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductChannelCosts",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CostTypes",
                schema: "catalog");

            migrationBuilder.AddColumn<string>(
                name: "TaxBase",
                schema: "catalog",
                table: "ProductChannelInputs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AppliedTaxRegime",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MarginAfterTax",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxBase",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxBaseAmount",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Vat",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxBase",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.DropColumn(
                name: "AppliedTaxRegime",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "MarginAfterTax",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "TaxBase",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "TaxBaseAmount",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "Vat",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.CreateTable(
                name: "CostTypes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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
    }
}
