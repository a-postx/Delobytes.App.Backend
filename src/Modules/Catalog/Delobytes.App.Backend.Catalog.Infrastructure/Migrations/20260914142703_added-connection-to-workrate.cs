using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addedconnectiontoworkrate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkRateId",
                schema: "catalog",
                table: "ProductWorkRates",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_WorkRateId",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "WorkRateId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductWorkRates_WorkRates_WorkRateId",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "WorkRateId",
                principalSchema: "catalog",
                principalTable: "WorkRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductWorkRates_WorkRates_WorkRateId",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropIndex(
                name: "IX_ProductWorkRates_WorkRateId",
                schema: "catalog",
                table: "ProductWorkRates");

            migrationBuilder.DropColumn(
                name: "WorkRateId",
                schema: "catalog",
                table: "ProductWorkRates");
        }
    }
}
