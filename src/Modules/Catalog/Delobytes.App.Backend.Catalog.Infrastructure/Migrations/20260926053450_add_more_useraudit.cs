using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_more_useraudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "WorkRates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "WorkRates",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "WorkRates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Suppliers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "Suppliers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "ComponentPrices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "ComponentPrices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "ComponentPrices",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "WorkRates");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "WorkRates");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "WorkRates");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "ComponentPrices");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "ComponentPrices");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "ComponentPrices");
        }
    }
}
