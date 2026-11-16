using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removed_legacy_entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelParameterSets",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "MarginCalculationSnapshots",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductChannelInputs",
                schema: "catalog");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelParameterSets",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelParameterSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelParameterSets_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "catalog",
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductChannelInputs",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PriceWithoutDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RawMaterialCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxBase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChannelInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductChannelInputs_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarginCalculationSnapshots",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductChannelInputId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCostSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquiringAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AppliedTaxRegime = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BuyerPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChannelCostTotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Margin = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MarginAfterTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MarginPercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    NetRevenue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxBase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TaxBaseAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    WorkRateId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarginCalculationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarginCalculationSnapshots_ProductChannelInputs_ProductChan~",
                        column: x => x.ProductChannelInputId,
                        principalSchema: "catalog",
                        principalTable: "ProductChannelInputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarginCalculationSnapshots_ProductCostSnapshots_ProductCost~",
                        column: x => x.ProductCostSnapshotId,
                        principalSchema: "catalog",
                        principalTable: "ProductCostSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelParameterSets_ChannelId_ValidFrom",
                schema: "catalog",
                table: "ChannelParameterSets",
                columns: new[] { "ChannelId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelParameterSets_TenantId",
                schema: "catalog",
                table: "ChannelParameterSets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_CalculatedAt",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "CalculatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_ProductChannelInputId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "ProductChannelInputId");

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_ProductCostSnapshotId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "ProductCostSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_TenantId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_ProductId",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_TenantId",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "TenantId");
        }
    }
}
