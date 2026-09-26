using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_useraudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "Products",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "ProductBarcodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "catalog",
                table: "ProductBarcodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "ProductBarcodes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "ProductBarcodes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "catalog",
                table: "Components",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Components",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "Components",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Channels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Channels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "catalog",
                table: "Channels",
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
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "ProductBarcodes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "catalog",
                table: "ProductBarcodes");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "ProductBarcodes");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "ProductBarcodes");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Components");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "catalog",
                table: "Components");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Components");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "Components");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "catalog",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "catalog",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "catalog",
                table: "Channels");
        }
    }
}
