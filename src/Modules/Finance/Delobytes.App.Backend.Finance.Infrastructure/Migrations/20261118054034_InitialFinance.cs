using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "PnlLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CommissionSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcquiringAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AcquiringSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayoutAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PayoutSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeliveryFeeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DeliveryFeeSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    NetRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    NetRevenueSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    FinancialPeriodFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    FinancialPeriodTo = table.Column<DateOnly>(type: "date", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PnlLines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PnlLines_TenantId",
                schema: "finance",
                table: "PnlLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PnlLines_TenantId_OrderLineId",
                schema: "finance",
                table: "PnlLines",
                columns: new[] { "TenantId", "OrderLineId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PnlLines",
                schema: "finance");
        }
    }
}
