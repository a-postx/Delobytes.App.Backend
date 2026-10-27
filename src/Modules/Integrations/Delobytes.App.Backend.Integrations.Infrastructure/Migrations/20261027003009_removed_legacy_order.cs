using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class removed_legacy_order : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrdersImportJobs",
                schema: "integrations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
        }
    }
}
