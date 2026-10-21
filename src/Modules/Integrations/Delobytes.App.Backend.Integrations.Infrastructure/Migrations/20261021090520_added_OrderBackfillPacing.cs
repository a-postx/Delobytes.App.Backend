using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_OrderBackfillPacing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CatchUpNextDay",
                schema: "integrations",
                table: "OrdersBackfillStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandoverWatermark",
                schema: "integrations",
                table: "OrdersBackfillStates",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CatchUpNextDay",
                schema: "integrations",
                table: "OrdersBackfillStates");

            migrationBuilder.DropColumn(
                name: "HandoverWatermark",
                schema: "integrations",
                table: "OrdersBackfillStates");
        }
    }
}
