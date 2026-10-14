using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_jobCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobCheckpoints",
                schema: "integrations",
                columns: table => new
                {
                    JobRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCheckpoints", x => x.JobRunId);
                    table.ForeignKey(
                        name: "FK_JobCheckpoints_JobRuns_JobRunId",
                        column: x => x.JobRunId,
                        principalSchema: "integrations",
                        principalTable: "JobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrdersImportJobs",
                schema: "integrations",
                columns: table => new
                {
                    JobRunId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdersImportJobs", x => x.JobRunId);
                    table.ForeignKey(
                        name: "FK_OrdersImportJobs_JobRuns_JobRunId",
                        column: x => x.JobRunId,
                        principalSchema: "integrations",
                        principalTable: "JobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductsImportJobs",
                schema: "integrations",
                columns: table => new
                {
                    JobRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalImportBatches = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductsImportJobs", x => x.JobRunId);
                    table.ForeignKey(
                        name: "FK_ProductsImportJobs_JobRuns_JobRunId",
                        column: x => x.JobRunId,
                        principalSchema: "integrations",
                        principalTable: "JobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncCheckpoints",
                schema: "integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<int>(type: "integer", nullable: false),
                    LastChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastJobRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncCheckpoints_Connections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalSchema: "integrations",
                        principalTable: "Connections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncCheckpoints_JobRuns_LastJobRunId",
                        column: x => x.LastJobRunId,
                        principalSchema: "integrations",
                        principalTable: "JobRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_ConnectionId_JobType",
                schema: "integrations",
                table: "SyncCheckpoints",
                columns: new[] { "ConnectionId", "JobType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_LastJobRunId",
                schema: "integrations",
                table: "SyncCheckpoints",
                column: "LastJobRunId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncCheckpoints_TenantId",
                schema: "integrations",
                table: "SyncCheckpoints",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobCheckpoints",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "OrdersImportJobs",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "ProductsImportJobs",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "SyncCheckpoints",
                schema: "integrations");
        }
    }
}
