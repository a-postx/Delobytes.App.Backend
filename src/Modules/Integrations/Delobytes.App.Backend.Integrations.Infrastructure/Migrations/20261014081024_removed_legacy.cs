using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removed_legacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncJobs_DateRangeFrom",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropIndex(
                name: "IX_Connections_LastSyncAt",
                schema: "integrations",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "DateRangeFrom",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "DateRangeTo",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "RecordsImported",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                schema: "integrations",
                table: "Connections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateRangeFrom",
                schema: "integrations",
                table: "SyncJobs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DateRangeTo",
                schema: "integrations",
                table: "SyncJobs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "RecordsImported",
                schema: "integrations",
                table: "SyncJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncAt",
                schema: "integrations",
                table: "Connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_DateRangeFrom",
                schema: "integrations",
                table: "SyncJobs",
                column: "DateRangeFrom");

            migrationBuilder.CreateIndex(
                name: "IX_Connections_LastSyncAt",
                schema: "integrations",
                table: "Connections",
                column: "LastSyncAt");
        }
    }
}
