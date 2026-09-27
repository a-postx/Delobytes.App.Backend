using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_syncjob_updated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncJobBatchResults",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncJobId = table.Column<Guid>(type: "uuid", nullable: false),
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncJobBatchResults",
                schema: "integrations");
        }
    }
}
