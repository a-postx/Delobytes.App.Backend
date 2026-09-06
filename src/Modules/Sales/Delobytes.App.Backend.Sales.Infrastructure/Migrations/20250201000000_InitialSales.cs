using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialSales : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "sales");

        migrationBuilder.CreateTable(
            name: "Orders",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ChannelProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                OrderDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                Revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Commission = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                NetRevenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RawDataId = table.Column<Guid>(type: "uuid", nullable: true),
                ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Returns",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                ReturnDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                RefundAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Returns", x => x.Id);
                table.ForeignKey(
                    name: "FK_Returns_Orders_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "sales",
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

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

        migrationBuilder.CreateIndex(
            name: "IX_Orders_TenantId",
            schema: "sales",
            table: "Orders",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_Returns_OrderId",
            schema: "sales",
            table: "Returns",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_Returns_ReturnDate",
            schema: "sales",
            table: "Returns",
            column: "ReturnDate");

        migrationBuilder.CreateIndex(
            name: "IX_Returns_TenantId",
            schema: "sales",
            table: "Returns",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Returns",
            schema: "sales");

        migrationBuilder.DropTable(
            name: "Orders",
            schema: "sales");
    }
}
