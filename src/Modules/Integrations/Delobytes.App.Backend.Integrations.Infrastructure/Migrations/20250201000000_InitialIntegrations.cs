using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialIntegrations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "integrations");

        migrationBuilder.CreateTable(
            name: "SystemChannelTemplates",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ApiBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ApiVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemChannelTemplates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Connections",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ApiSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Settings = table.Column<string>(type: "text", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Connections", x => x.Id);
                table.ForeignKey(
                    name: "FK_Connections_SystemChannelTemplates_ChannelId",
                    column: x => x.ChannelId,
                    principalSchema: "integrations",
                    principalTable: "SystemChannelTemplates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SyncJobs",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                JobType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DateRangeFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DateRangeTo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RecordsProcessed = table.Column<int>(type: "integer", nullable: false),
                RecordsImported = table.Column<int>(type: "integer", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SyncJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_SyncJobs_Connections_ConnectionId",
                    column: x => x.ConnectionId,
                    principalSchema: "integrations",
                    principalTable: "Connections",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RawApiResponses",
            schema: "integrations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SyncJobId = table.Column<Guid>(type: "uuid", nullable: false),
                Endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                RequestPayload = table.Column<string>(type: "text", nullable: false),
                ResponsePayload = table.Column<string>(type: "text", nullable: false),
                HttpStatusCode = table.Column<int>(type: "integer", nullable: false),
                ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RawApiResponses", x => x.Id);
                table.ForeignKey(
                    name: "FK_RawApiResponses_SyncJobs_SyncJobId",
                    column: x => x.SyncJobId,
                    principalSchema: "integrations",
                    principalTable: "SyncJobs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Connections_ChannelId",
            schema: "integrations",
            table: "Connections",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_Connections_IsActive",
            schema: "integrations",
            table: "Connections",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_Connections_LastSyncAt",
            schema: "integrations",
            table: "Connections",
            column: "LastSyncAt");

        migrationBuilder.CreateIndex(
            name: "IX_Connections_TenantId",
            schema: "integrations",
            table: "Connections",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_RawApiResponses_HttpStatusCode",
            schema: "integrations",
            table: "RawApiResponses",
            column: "HttpStatusCode");

        migrationBuilder.CreateIndex(
            name: "IX_RawApiResponses_ProcessedAt",
            schema: "integrations",
            table: "RawApiResponses",
            column: "ProcessedAt");

        migrationBuilder.CreateIndex(
            name: "IX_RawApiResponses_ReceivedAt",
            schema: "integrations",
            table: "RawApiResponses",
            column: "ReceivedAt");

        migrationBuilder.CreateIndex(
            name: "IX_RawApiResponses_SyncJobId",
            schema: "integrations",
            table: "RawApiResponses",
            column: "SyncJobId");

        migrationBuilder.CreateIndex(
            name: "IX_RawApiResponses_TenantId",
            schema: "integrations",
            table: "RawApiResponses",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_SyncJobs_ConnectionId",
            schema: "integrations",
            table: "SyncJobs",
            column: "ConnectionId");

        migrationBuilder.CreateIndex(
            name: "IX_SyncJobs_DateRangeFrom",
            schema: "integrations",
            table: "SyncJobs",
            column: "DateRangeFrom");

        migrationBuilder.CreateIndex(
            name: "IX_SyncJobs_JobType",
            schema: "integrations",
            table: "SyncJobs",
            column: "JobType");

        migrationBuilder.CreateIndex(
            name: "IX_SyncJobs_StartedAt",
            schema: "integrations",
            table: "SyncJobs",
            column: "StartedAt");

        migrationBuilder.CreateIndex(
            name: "IX_SyncJobs_Status",
            schema: "integrations",
            table: "SyncJobs",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_SyncJobs_TenantId",
            schema: "integrations",
            table: "SyncJobs",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_SystemChannelTemplates_Code",
            schema: "integrations",
            table: "SystemChannelTemplates",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SystemChannelTemplates_IsActive",
            schema: "integrations",
            table: "SystemChannelTemplates",
            column: "IsActive");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RawApiResponses",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "SyncJobs",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "Connections",
            schema: "integrations");

        migrationBuilder.DropTable(
            name: "SystemChannelTemplates",
            schema: "integrations");
    }
}
