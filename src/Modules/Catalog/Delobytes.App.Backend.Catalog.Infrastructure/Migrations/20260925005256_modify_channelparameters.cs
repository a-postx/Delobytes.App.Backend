using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class modify_channelparameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductChannelInputs_ChannelParameterSets_ChannelParameterS~",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.DropIndex(
                name: "IX_ProductChannelInputs_ChannelParameterSetId",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.DropIndex(
                name: "IX_ProductChannelInputs_ProductId_ChannelParameterSetId_ValidF~",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.DropColumn(
                name: "ChannelParameterSetId",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.DropColumn(
                name: "AcquiringPercent",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "CommissionPercent",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "SppEnabled",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.DropColumn(
                name: "SppPercent",
                schema: "catalog",
                table: "ChannelParameterSets");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_ProductId",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductChannelInputs_ProductId",
                schema: "catalog",
                table: "ProductChannelInputs");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelParameterSetId",
                schema: "catalog",
                table: "ProductChannelInputs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "AcquiringPercent",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPercent",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "SppEnabled",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SppPercent",
                schema: "catalog",
                table: "ChannelParameterSets",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_ChannelParameterSetId",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "ChannelParameterSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_ProductId_ChannelParameterSetId_ValidF~",
                schema: "catalog",
                table: "ProductChannelInputs",
                columns: new[] { "ProductId", "ChannelParameterSetId", "ValidFrom" });

            migrationBuilder.AddForeignKey(
                name: "FK_ProductChannelInputs_ChannelParameterSets_ChannelParameterS~",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "ChannelParameterSetId",
                principalSchema: "catalog",
                principalTable: "ChannelParameterSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
