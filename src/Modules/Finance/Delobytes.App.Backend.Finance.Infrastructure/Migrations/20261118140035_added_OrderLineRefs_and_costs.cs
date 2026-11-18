using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_OrderLineRefs_and_costs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CompensationAmount",
                schema: "finance",
                table: "PnlLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompensationSource",
                schema: "finance",
                table: "PnlLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ProductionCostAmount",
                schema: "finance",
                table: "PnlLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProductionCostIsComplete",
                schema: "finance",
                table: "PnlLines",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SaleDate",
                schema: "finance",
                table: "PnlLines",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderLineCosts",
                schema: "finance",
                columns: table => new
                {
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitMaterialCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitLogisticsCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitPackagingCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitLaborCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    UnitTotalCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    TotalCost = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                    WarningTypes = table.Column<List<string>>(type: "text[]", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLineCosts", x => x.OrderLineId);
                });

            migrationBuilder.CreateTable(
                name: "OrderLineRefs",
                schema: "finance",
                columns: table => new
                {
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalLineId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChannelProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    OrderDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLineRefs", x => x.OrderLineId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineCosts_TenantId",
                schema: "finance",
                table: "OrderLineCosts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineRefs_TenantId",
                schema: "finance",
                table: "OrderLineRefs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineRefs_TenantId_ChannelId_ExternalLineId",
                schema: "finance",
                table: "OrderLineRefs",
                columns: new[] { "TenantId", "ChannelId", "ExternalLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineRefs_TenantId_ChannelId_ExternalOrderId_ExternalPr~",
                schema: "finance",
                table: "OrderLineRefs",
                columns: new[] { "TenantId", "ChannelId", "ExternalOrderId", "ExternalProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLineCosts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "OrderLineRefs",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "CompensationAmount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "CompensationSource",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "ProductionCostAmount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "ProductionCostIsComplete",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "SaleDate",
                schema: "finance",
                table: "PnlLines");
        }
    }
}
