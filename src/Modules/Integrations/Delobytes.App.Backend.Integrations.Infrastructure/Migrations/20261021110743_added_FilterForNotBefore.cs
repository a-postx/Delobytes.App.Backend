using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_FilterForNotBefore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobRuns_ActivePerConnectionType",
                schema: "integrations",
                table: "JobRuns");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_ActivePerConnectionType",
                schema: "integrations",
                table: "JobRuns",
                columns: new[] { "ConnectionId", "JobType" },
                unique: true,
                filter: "\"Status\" IN (0, 1) AND \"NotBefore\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobRuns_ActivePerConnectionType",
                schema: "integrations",
                table: "JobRuns");

            migrationBuilder.CreateIndex(
                name: "IX_JobRuns_ActivePerConnectionType",
                schema: "integrations",
                table: "JobRuns",
                columns: new[] { "ConnectionId", "JobType" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");
        }
    }
}
