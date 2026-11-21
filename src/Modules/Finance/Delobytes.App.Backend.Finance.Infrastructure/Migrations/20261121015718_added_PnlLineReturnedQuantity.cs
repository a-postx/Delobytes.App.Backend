using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_PnlLineReturnedQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnedQuantity",
                schema: "finance",
                table: "PnlLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                schema: "finance",
                table: "PnlLines");
        }
    }
}
