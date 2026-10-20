using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_OrderBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrdersBackfillStates",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    NextDayToFetch = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OldestDayInScope = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxObservedLastChangeDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdersBackfillStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrdersBackfillStates_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integrations",
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WildberriesMethodThrottles",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    NextRequestAllowedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    LastLimit = table.Column<int>(type: "integer", nullable: true),
                    LastRemaining = table.Column<int>(type: "integer", nullable: true),
                    LastRetryAfterSeconds = table.Column<int>(type: "integer", nullable: true),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    ObservedWindowSeconds = table.Column<int>(type: "integer", nullable: true),
                    SuccessStreak = table.Column<int>(type: "integer", nullable: false),
                    LastRequestAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WildberriesMethodThrottles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrdersBackfillStates_ConnectionId",
                schema: "integrations",
                table: "OrdersBackfillStates",
                column: "ConnectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdersBackfillStates_TenantId",
                schema: "integrations",
                table: "OrdersBackfillStates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WildberriesMethodThrottles_KeyFingerprint_Method",
                schema: "integrations",
                table: "WildberriesMethodThrottles",
                columns: new[] { "KeyFingerprint", "Method" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrdersBackfillStates",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "WildberriesMethodThrottles",
                schema: "integrations");
        }
    }
}
