using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removed_column : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxObservedLastChangeDate",
                schema: "integrations",
                table: "OrdersBackfillStates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MaxObservedLastChangeDate",
                schema: "integrations",
                table: "OrdersBackfillStates",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
