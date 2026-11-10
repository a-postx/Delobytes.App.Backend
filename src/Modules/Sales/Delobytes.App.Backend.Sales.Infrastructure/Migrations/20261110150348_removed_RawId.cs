using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removed_RawId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RawDataId",
                schema: "sales",
                table: "Returns");

            migrationBuilder.DropColumn(
                name: "RawDataId",
                schema: "sales",
                table: "OrderSettlements");

            migrationBuilder.DropColumn(
                name: "RawDataId",
                schema: "sales",
                table: "Orders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RawDataId",
                schema: "sales",
                table: "Returns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RawDataId",
                schema: "sales",
                table: "OrderSettlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RawDataId",
                schema: "sales",
                table: "Orders",
                type: "uuid",
                nullable: true);
        }
    }
}
