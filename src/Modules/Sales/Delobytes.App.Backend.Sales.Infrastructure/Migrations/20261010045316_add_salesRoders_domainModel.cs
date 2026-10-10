using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_salesRoders_domainModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Returns_Orders_OrderId",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Returns_ReturnDate",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ChannelId",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ChannelProductId",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_ExternalOrderId_ChannelId",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderDate",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ChannelProductId",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Commission",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "NetRevenue",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Quantity",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Revenue",
                schema: "sales",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "OrderId",
                schema: "sales",
                table: "Returns",
                newName: "OrderLineId");

            migrationBuilder.RenameIndex(
                name: "IX_Returns_OrderId",
                schema: "sales",
                table: "Returns",
                newName: "IX_Returns_OrderLineId");

            migrationBuilder.RenameColumn(
                name: "ImportedAt",
                schema: "sales",
                table: "Orders",
                newName: "LastImportedAt");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundAmount",
                schema: "sales",
                table: "Returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "sales",
                table: "Returns",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalReturnId",
                schema: "sales",
                table: "Returns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "sales",
                table: "Returns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "RawDataId",
                schema: "sales",
                table: "Returns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundSource",
                schema: "sales",
                table: "Returns",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "sales",
                table: "Orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "sales",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ChannelReportedDeliveryFee",
                schema: "sales",
                table: "Orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ChannelReportedTotal",
                schema: "sales",
                table: "Orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                schema: "sales",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "sales",
                table: "Orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCity",
                schema: "sales",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryRegion",
                schema: "sales",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOrderNumber",
                schema: "sales",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalStatus",
                schema: "sales",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubstatus",
                schema: "sales",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstImportedAt",
                schema: "sales",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<bool>(
                name: "IsB2b",
                schema: "sales",
                table: "Orders",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSellerWarehouse",
                schema: "sales",
                table: "Orders",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTestOrder",
                schema: "sales",
                table: "Orders",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StatusChangedAt",
                schema: "sales",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "sales",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseName",
                schema: "sales",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "sales",
                table: "Orders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ChannelProductRefs",
                schema: "sales",
                columns: table => new
                {
                    ChannelProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalProductId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PhotoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelProductRefs", x => x.ChannelProductId);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalLineId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OfferId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalSizeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Vat = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    BuyerUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    UnitPriceBeforeDiscount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ChannelProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLines_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "sales",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderSettlements",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CommissionSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    RawDataId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "IX_Returns_TenantId_OrderLineId",
                schema: "sales",
                table: "Returns",
                columns: new[] { "TenantId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_Returns_TenantId_ReturnDate",
                schema: "sales",
                table: "Returns",
                columns: new[] { "TenantId", "ReturnDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_ChannelId_ExternalOrderId",
                schema: "sales",
                table: "Orders",
                columns: new[] { "TenantId", "ChannelId", "ExternalOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_ChannelId_OrderDate",
                schema: "sales",
                table: "Orders",
                columns: new[] { "TenantId", "ChannelId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_OrderDate",
                schema: "sales",
                table: "Orders",
                columns: new[] { "TenantId", "OrderDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TenantId_Status",
                schema: "sales",
                table: "Orders",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductRefs_TenantId",
                schema: "sales",
                table: "ChannelProductRefs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductRefs_TenantId_ChannelId_ExternalProductId",
                schema: "sales",
                table: "ChannelProductRefs",
                columns: new[] { "TenantId", "ChannelId", "ExternalProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductRefs_TenantId_ProductId",
                schema: "sales",
                table: "ChannelProductRefs",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                schema: "sales",
                table: "OrderLines",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_TenantId",
                schema: "sales",
                table: "OrderLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_TenantId_ChannelProductId",
                schema: "sales",
                table: "OrderLines",
                columns: new[] { "TenantId", "ChannelProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_TenantId_OrderId_LineKey",
                schema: "sales",
                table: "OrderLines",
                columns: new[] { "TenantId", "OrderId", "LineKey" },
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_Returns_OrderLines_OrderLineId",
                schema: "sales",
                table: "Returns",
                column: "OrderLineId",
                principalSchema: "sales",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Returns_OrderLines_OrderLineId",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropTable(
                name: "ChannelProductRefs",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "OrderSettlements",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "OrderLines",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "IX_Returns_TenantId_OrderLineId",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Returns_TenantId_ReturnDate",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_ChannelId_ExternalOrderId",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_ChannelId_OrderDate",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_OrderDate",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_TenantId_Status",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "ExternalReturnId",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "RawDataId",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "RefundSource",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ChannelReportedDeliveryFee",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ChannelReportedTotal",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryCity",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryRegion",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalOrderNumber",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalStatus",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExternalSubstatus",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FirstImportedAt",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsB2b",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsSellerWarehouse",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsTestOrder",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StatusChangedAt",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "WarehouseName",
                schema: "sales",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "sales",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "OrderLineId",
                schema: "sales",
                table: "Returns",
                newName: "OrderId");

            migrationBuilder.RenameIndex(
                name: "IX_Returns_OrderLineId",
                schema: "sales",
                table: "Returns",
                newName: "IX_Returns_OrderId");

            migrationBuilder.RenameColumn(
                name: "LastImportedAt",
                schema: "sales",
                table: "Orders",
                newName: "ImportedAt");

            migrationBuilder.AlterColumn<decimal>(
                name: "RefundAmount",
                schema: "sales",
                table: "Returns",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "sales",
                table: "Orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelProductId",
                schema: "sales",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "Commission",
                schema: "sales",
                table: "Orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetRevenue",
                schema: "sales",
                table: "Orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                schema: "sales",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "Revenue",
                schema: "sales",
                table: "Orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Returns_ReturnDate",
                schema: "sales",
                table: "Returns",
                column: "ReturnDate");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ChannelId",
                schema: "sales",
                table: "Orders",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ChannelProductId",
                schema: "sales",
                table: "Orders",
                column: "ChannelProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ExternalOrderId_ChannelId",
                schema: "sales",
                table: "Orders",
                columns: new[] { "ExternalOrderId", "ChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderDate",
                schema: "sales",
                table: "Orders",
                column: "OrderDate");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                schema: "sales",
                table: "Orders",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Returns_Orders_OrderId",
                schema: "sales",
                table: "Returns",
                column: "OrderId",
                principalSchema: "sales",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
