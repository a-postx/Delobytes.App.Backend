using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class changed_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ChannelProducts_ExternalProductId",
                schema: "catalog",
                table: "ChannelProducts");

            migrationBuilder.DropIndex(
                name: "IX_ChannelProducts_IsActive",
                schema: "catalog",
                table: "ChannelProducts");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Sku",
                schema: "catalog",
                table: "Products",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProducts_Channel_ExternalProduct",
                schema: "catalog",
                table: "ChannelProducts",
                columns: new[] { "ChannelId", "ExternalProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_Sku",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ChannelProducts_Channel_ExternalProduct",
                schema: "catalog",
                table: "ChannelProducts");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                schema: "catalog",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProducts_ExternalProductId",
                schema: "catalog",
                table: "ChannelProducts",
                column: "ExternalProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProducts_IsActive",
                schema: "catalog",
                table: "ChannelProducts",
                column: "IsActive");
        }
    }
}
