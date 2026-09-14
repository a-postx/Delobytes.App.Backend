using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class changedvattype : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatApplicable",
                schema: "identity",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "VatPercent",
                schema: "identity",
                table: "Tenants");

            migrationBuilder.AddColumn<int>(
                name: "VatType",
                schema: "identity",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VatType",
                schema: "identity",
                table: "Tenants");

            migrationBuilder.AddColumn<bool>(
                name: "VatApplicable",
                schema: "identity",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatPercent",
                schema: "identity",
                table: "Tenants",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
