using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class rawResponse_wired_to_channel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                schema: "integrations",
                table: "RawApiResponses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChannelRequestId",
                schema: "integrations",
                table: "RawApiResponses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                schema: "integrations",
                table: "RawApiResponses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawApiResponses_ChannelRequestId",
                schema: "integrations",
                table: "RawApiResponses",
                column: "ChannelRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_RawApiResponses_ConnectionId",
                schema: "integrations",
                table: "RawApiResponses",
                column: "ConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RawApiResponses_ChannelRequestId",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropIndex(
                name: "IX_RawApiResponses_ConnectionId",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropColumn(
                name: "ChannelRequestId",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                schema: "integrations",
                table: "RawApiResponses");
        }
    }
}
