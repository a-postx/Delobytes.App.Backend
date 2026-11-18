using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_OrderLinesExternalProductIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_TenantId_ExternalProductId",
                schema: "sales",
                table: "OrderLines",
                columns: new[] { "TenantId", "ExternalProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderLines_TenantId_ExternalProductId",
                schema: "sales",
                table: "OrderLines");
        }
    }
}
