using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_jobRunBatchResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RawApiResponses_SyncJobs_SyncJobId",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropTable(
                name: "SyncJobBatchResults",
                schema: "integrations");

            migrationBuilder.DropIndex(
                name: "IX_RawApiResponses_ProcessedAt",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.RenameColumn(
                name: "SyncJobId",
                schema: "integrations",
                table: "RawApiResponses",
                newName: "JobRunId");

            migrationBuilder.RenameIndex(
                name: "IX_RawApiResponses_SyncJobId",
                schema: "integrations",
                table: "RawApiResponses",
                newName: "IX_RawApiResponses_JobRunId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncAt",
                schema: "integrations",
                table: "Connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobRunBatchResults",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordsProcessed = table.Column<int>(type: "integer", nullable: false),
                    RecordsCreated = table.Column<int>(type: "integer", nullable: false),
                    RecordsUpdated = table.Column<int>(type: "integer", nullable: false),
                    RecordsSkipped = table.Column<int>(type: "integer", nullable: false),
                    RecordsFailed = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsLastBatch = table.Column<bool>(type: "boolean", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRunBatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobRunBatchResults_JobRuns_JobRunId",
                        column: x => x.JobRunId,
                        principalSchema: "integrations",
                        principalTable: "JobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Connections_LastSyncAt",
                schema: "integrations",
                table: "Connections",
                column: "LastSyncAt");

            migrationBuilder.CreateIndex(
                name: "IX_JobRunBatchResults_JobRunId",
                schema: "integrations",
                table: "JobRunBatchResults",
                column: "JobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRunBatchResults_MessageId",
                schema: "integrations",
                table: "JobRunBatchResults",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobRunBatchResults_TenantId",
                schema: "integrations",
                table: "JobRunBatchResults",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_RawApiResponses_JobRuns_JobRunId",
                schema: "integrations",
                table: "RawApiResponses",
                column: "JobRunId",
                principalSchema: "integrations",
                principalTable: "JobRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RawApiResponses_JobRuns_JobRunId",
                schema: "integrations",
                table: "RawApiResponses");

            migrationBuilder.DropTable(
                name: "JobRunBatchResults",
                schema: "integrations");

            migrationBuilder.DropIndex(
                name: "IX_Connections_LastSyncAt",
                schema: "integrations",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                schema: "integrations",
                table: "Connections");

            migrationBuilder.RenameColumn(
                name: "JobRunId",
                schema: "integrations",
                table: "RawApiResponses",
                newName: "SyncJobId");

            migrationBuilder.RenameIndex(
                name: "IX_RawApiResponses_JobRunId",
                schema: "integrations",
                table: "RawApiResponses",
                newName: "IX_RawApiResponses_SyncJobId");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProcessedAt",
                schema: "integrations",
                table: "RawApiResponses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncJobBatchResults",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsLastBatch = table.Column<bool>(type: "boolean", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordsCreated = table.Column<int>(type: "integer", nullable: false),
                    RecordsFailed = table.Column<int>(type: "integer", nullable: false),
                    RecordsProcessed = table.Column<int>(type: "integer", nullable: false),
                    RecordsSkipped = table.Column<int>(type: "integer", nullable: false),
                    RecordsUpdated = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobBatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncJobBatchResults_SyncJobs_SyncJobId",
                        column: x => x.SyncJobId,
                        principalSchema: "integrations",
                        principalTable: "SyncJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RawApiResponses_ProcessedAt",
                schema: "integrations",
                table: "RawApiResponses",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobBatchResults_MessageId",
                schema: "integrations",
                table: "SyncJobBatchResults",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobBatchResults_SyncJobId",
                schema: "integrations",
                table: "SyncJobBatchResults",
                column: "SyncJobId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobBatchResults_TenantId",
                schema: "integrations",
                table: "SyncJobBatchResults",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_RawApiResponses_SyncJobs_SyncJobId",
                schema: "integrations",
                table: "RawApiResponses",
                column: "SyncJobId",
                principalSchema: "integrations",
                principalTable: "SyncJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
