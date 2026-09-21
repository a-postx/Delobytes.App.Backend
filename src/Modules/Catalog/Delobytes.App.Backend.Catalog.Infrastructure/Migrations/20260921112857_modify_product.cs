using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class modify_product : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                schema: "catalog",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "catalog",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletionRequestedAt",
                schema: "catalog",
                table: "Products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "catalog",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Status",
                schema: "catalog",
                table: "Products",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Status_DeletionRequestedAt",
                schema: "catalog",
                table: "Products",
                columns: new[] { "Status", "DeletionRequestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Status",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Status_DeletionRequestedAt",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAt",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "catalog",
                table: "Products");
        }
    }
}
