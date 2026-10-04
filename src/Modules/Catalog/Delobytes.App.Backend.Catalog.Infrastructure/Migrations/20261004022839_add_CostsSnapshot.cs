using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_CostsSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaterialLogisticsCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "RawMaterialCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "TotalCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "WorkCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.AddColumn<Guid>(
                name: "ProductCostSnapshotId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "ProductCostSnapshots",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MaterialCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LogisticsCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagingCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LaborCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    LinesSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    TriggerReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCostSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductCostSnapshots_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_ProductCostSnapshotId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "ProductCostSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostSnapshots_ProductId_AsOfDate",
                schema: "catalog",
                table: "ProductCostSnapshots",
                columns: new[] { "ProductId", "AsOfDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCostSnapshots_TenantId",
                schema: "catalog",
                table: "ProductCostSnapshots",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_MarginCalculationSnapshots_ProductCostSnapshots_ProductCost~",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "ProductCostSnapshotId",
                principalSchema: "catalog",
                principalTable: "ProductCostSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarginCalculationSnapshots_ProductCostSnapshots_ProductCost~",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropTable(
                name: "ProductCostSnapshots",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_MarginCalculationSnapshots_ProductCostSnapshotId",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "ProductCostSnapshotId",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.AddColumn<decimal>(
                name: "MaterialLogisticsCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RawMaterialCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
