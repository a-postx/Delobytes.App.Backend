using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class modify_product_photy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                schema: "catalog",
                table: "ProductPhotos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                schema: "catalog",
                table: "ProductPhotos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCheckedAt",
                schema: "catalog",
                table: "ProductPhotos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                schema: "catalog",
                table: "ProductPhotos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ChannelId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "TenantId", "ProductId", "ChannelId", "ExternalId" },
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ContentHash",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "TenantId", "ProductId", "ContentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ChannelId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ContentHash",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropColumn(
                name: "LastCheckedAt",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "TenantId", "ProductId", "ExternalId" },
                unique: true);
        }
    }
}
