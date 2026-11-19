using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_PnlLineExpectationsAndCompleteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletenessState",
                schema: "finance",
                table: "PnlLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Complete");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpectedBy",
                schema: "finance",
                table: "PnlLines",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "MissingDocumentTypes",
                schema: "finance",
                table: "PnlLines",
                type: "text[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::text[]");

            migrationBuilder.CreateTable(
                name: "PnlLineExpectations",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExpectedBy = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AmountEstimate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosureReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PnlLineExpectations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLineCosts_TenantId_ProductId",
                schema: "finance",
                table: "OrderLineCosts",
                columns: new[] { "TenantId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_PnlLineExpectations_TenantId",
                schema: "finance",
                table: "PnlLineExpectations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PnlLineExpectations_TenantId_OrderLineId_ExpectedType",
                schema: "finance",
                table: "PnlLineExpectations",
                columns: new[] { "TenantId", "OrderLineId", "ExpectedType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PnlLineExpectations_TenantId_State_ExpectedBy",
                schema: "finance",
                table: "PnlLineExpectations",
                columns: new[] { "TenantId", "State", "ExpectedBy" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PnlLineExpectations",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "IX_OrderLineCosts_TenantId_ProductId",
                schema: "finance",
                table: "OrderLineCosts");

            migrationBuilder.DropColumn(
                name: "CompletenessState",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "ExpectedBy",
                schema: "finance",
                table: "PnlLines");

            migrationBuilder.DropColumn(
                name: "MissingDocumentTypes",
                schema: "finance",
                table: "PnlLines");
        }
    }
}
