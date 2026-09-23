using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class modify_channels_fix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Connections_SystemChannelTemplates_ChannelId",
                schema: "integrations",
                table: "Connections");

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_SystemChannelTemplates_SystemChannelTemplateId",
                schema: "integrations",
                table: "Connections",
                column: "SystemChannelTemplateId",
                principalSchema: "integrations",
                principalTable: "SystemChannelTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Connections_SystemChannelTemplates_SystemChannelTemplateId",
                schema: "integrations",
                table: "Connections");

            migrationBuilder.AddForeignKey(
                name: "FK_Connections_SystemChannelTemplates_ChannelId",
                schema: "integrations",
                table: "Connections",
                column: "ChannelId",
                principalSchema: "integrations",
                principalTable: "SystemChannelTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
