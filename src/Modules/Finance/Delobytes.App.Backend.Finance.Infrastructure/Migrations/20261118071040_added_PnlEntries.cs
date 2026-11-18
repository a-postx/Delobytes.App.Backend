using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_PnlEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayoutSource",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "PayoutAmount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.AddColumn<decimal>(
                name: "RevenueAmount",
                schema: "finance",
                table: "PnlLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevenueSource",
                schema: "finance",
                table: "PnlLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ComputedAt",
                schema: "finance",
                table: "PnlLines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ControlDifferenceAmount",
                schema: "finance",
                table: "PnlLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ControlTotalAmount",
                schema: "finance",
                table: "PnlLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntryCount",
                schema: "finance",
                table: "PnlLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PnlEntries",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReportInstanceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalEntryId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalOrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalLineId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExternalProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupersededByEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PnlEntries", x => x.Id);
                    table.CheckConstraint("CK_PnlEntries_AmountNonNegative", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_PnlEntries_LineOrOrder", "\"ExternalLineId\" IS NOT NULL OR \"ExternalOrderId\" IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId",
                schema: "finance",
                table: "PnlEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId_ChannelId_ExternalLineId",
                schema: "finance",
                table: "PnlEntries",
                columns: new[] { "TenantId", "ChannelId", "ExternalLineId" },
                filter: "\"SupersededByEntryId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId_ChannelId_ExternalOrderId_ExternalProdu~",
                schema: "finance",
                table: "PnlEntries",
                columns: new[] { "TenantId", "ChannelId", "ExternalOrderId", "ExternalProductId" },
                filter: "\"SupersededByEntryId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId_DocumentDate",
                schema: "finance",
                table: "PnlEntries",
                columns: new[] { "TenantId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId_Source_ReportInstanceKey_ExternalEntryI~",
                schema: "finance",
                table: "PnlEntries",
                columns: new[] { "TenantId", "Source", "ReportInstanceKey", "ExternalEntryId", "ExternalProductId", "Kind", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PnlEntries",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "ComputedAt",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "ControlDifferenceAmount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "ControlTotalAmount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "EntryCount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "RevenueSource",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "RevenueAmount",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.AddColumn<decimal>(
                name: "PayoutAmount",
                schema: "finance",
                table: "PnlLines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutSource",
                schema: "finance",
                table: "PnlLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");
        }
    }
}
