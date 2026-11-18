using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removed_OrderSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderSettlements",
                schema: "sales");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderSettlements",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcquiringAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AcquiringSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CommissionSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DeliveryFeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DeliveryFeeSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FinancialPeriodFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    FinancialPeriodTo = table.Column<DateOnly>(type: "date", nullable: true),
                    NetRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NetRevenueSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayoutAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PayoutSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderSettlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderSettlements_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalSchema: "sales",
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderSettlements_OrderLineId",
                schema: "sales",
                table: "OrderSettlements",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSettlements_TenantId",
                schema: "sales",
                table: "OrderSettlements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderSettlements_TenantId_OrderLineId",
                schema: "sales",
                table: "OrderSettlements",
                columns: new[] { "TenantId", "OrderLineId" },
                unique: true);
        }
    }
}
