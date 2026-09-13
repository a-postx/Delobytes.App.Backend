using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removedproperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "TaxType",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "VatApplicable",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "VatPercent",
                schema: "catalog",
                table: "ChannelParameterSets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TaxType",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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
        }
    }
}
