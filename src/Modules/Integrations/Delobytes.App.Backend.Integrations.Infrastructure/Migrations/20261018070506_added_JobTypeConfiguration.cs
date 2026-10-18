using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_JobTypeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobTypeConfigurations",
                schema: "integrations",
                columns: table => new
                {
                    JobType = table.Column<int>(type: "integer", nullable: false),
                    TimeoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTypeConfigurations", x => x.JobType);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobTypeConfigurations",
                schema: "integrations");
        }
    }
}
