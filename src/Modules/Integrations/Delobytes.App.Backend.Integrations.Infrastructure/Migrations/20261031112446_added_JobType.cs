using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_JobType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdersBackfillStates_ConnectionId",
                schema: "integrations",
                table: "OrdersBackfillStates");

            migrationBuilder.AddColumn<int>(
                name: "JobType",
                schema: "integrations",
                table: "OrdersBackfillStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OrdersBackfillStates_ConnectionId_JobType",
                schema: "integrations",
                table: "OrdersBackfillStates",
                columns: new[] { "ConnectionId", "JobType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrdersBackfillStates_ConnectionId_JobType",
                schema: "integrations",
                table: "OrdersBackfillStates");

            migrationBuilder.DropColumn(
                name: "JobType",
                schema: "integrations",
                table: "OrdersBackfillStates");

            migrationBuilder.CreateIndex(
                name: "IX_OrdersBackfillStates_ConnectionId",
                schema: "integrations",
                table: "OrdersBackfillStates",
                column: "ConnectionId",
                unique: true);
        }
    }
}
