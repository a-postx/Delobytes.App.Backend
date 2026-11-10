using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_AcquiringAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Vat",
                schema: "sales",
                table: "OrderLines");

            migrationBuilder.AddColumn<decimal>(
                name: "AcquiringAmount",
                schema: "sales",
                table: "OrderSettlements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcquiringSource",
                schema: "sales",
                table: "OrderSettlements",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquiringAmount",
                schema: "sales",
                table: "OrderSettlements");

            migrationBuilder.DropColumn(
                name: "AcquiringSource",
                schema: "sales",
                table: "OrderSettlements");

            migrationBuilder.AddColumn<string>(
                name: "Vat",
                schema: "sales",
                table: "OrderLines",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }
    }
}
