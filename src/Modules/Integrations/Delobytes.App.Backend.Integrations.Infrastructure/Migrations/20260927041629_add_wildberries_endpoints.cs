using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_wildberries_endpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NextCursor",
                schema: "integrations",
                table: "SyncJobs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecordsCreated",
                schema: "integrations",
                table: "SyncJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecordsFailed",
                schema: "integrations",
                table: "SyncJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecordsSkipped",
                schema: "integrations",
                table: "SyncJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RecordsUpdated",
                schema: "integrations",
                table: "SyncJobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SystemChannelEndpoints",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemChannelTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointType = table.Column<int>(type: "integer", nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemChannelEndpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemChannelEndpoints_SystemChannelTemplates_SystemChannel~",
                        column: x => x.SystemChannelTemplateId,
                        principalSchema: "integrations",
                        principalTable: "SystemChannelTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemChannelEndpoints_IsActive",
                schema: "integrations",
                table: "SystemChannelEndpoints",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SystemChannelEndpoints_SystemChannelTemplateId_EndpointType",
                schema: "integrations",
                table: "SystemChannelEndpoints",
                columns: new[] { "SystemChannelTemplateId", "EndpointType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemChannelEndpoints",
                schema: "integrations");

            migrationBuilder.DropColumn(
                name: "NextCursor",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "RecordsCreated",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "RecordsFailed",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "RecordsSkipped",
                schema: "integrations",
                table: "SyncJobs");

            migrationBuilder.DropColumn(
                name: "RecordsUpdated",
                schema: "integrations",
                table: "SyncJobs");
        }
    }
}
