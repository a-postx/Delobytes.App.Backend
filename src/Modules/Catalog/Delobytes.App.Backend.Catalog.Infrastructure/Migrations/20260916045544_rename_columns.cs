using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class rename_columns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PackagingWorkCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "WorkCost");

            migrationBuilder.RenameColumn(
                name: "PackagingCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "MaterialLogisticsCost");

            migrationBuilder.RenameColumn(
                name: "LogisticsToMarketplaceCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "LogisticsToCustomerCost");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "WorkCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "PackagingWorkCost");

            migrationBuilder.RenameColumn(
                name: "MaterialLogisticsCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "PackagingCost");

            migrationBuilder.RenameColumn(
                name: "LogisticsToCustomerCost",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                newName: "LogisticsToMarketplaceCost");
        }
    }
}
