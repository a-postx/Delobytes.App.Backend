using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComponentPriceQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing price versions were entered as the price of exactly one unit, so 1 keeps
            // their meaning. The default is dropped right after: new rows must state the quantity,
            // and a silent 0 would make every cost calculation of that component unusable.
            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                schema: "catalog",
                table: "ComponentPrices",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                schema: "catalog",
                table: "ComponentPrices",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldDefaultValue: 1m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Quantity",
                schema: "catalog",
                table: "ComponentPrices");
        }
    }
}
