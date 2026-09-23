using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class modify_channels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SystemChannelTemplateId",
                schema: "integrations",
                table: "Connections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Connections_SystemChannelTemplateId",
                schema: "integrations",
                table: "Connections",
                column: "SystemChannelTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Connections_SystemChannelTemplateId",
                schema: "integrations",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "SystemChannelTemplateId",
                schema: "integrations",
                table: "Connections");
        }
    }
}
