using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addedindex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VatApplicable",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercent",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId_Inn",
                schema: "catalog",
                table: "Suppliers",
                columns: new[] { "TenantId", "Inn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_TenantId_Inn",
                schema: "catalog",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "VatApplicable",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "VatPercent",
                schema: "catalog",
                table: "ChannelParameterSets");
        }
    }
}
