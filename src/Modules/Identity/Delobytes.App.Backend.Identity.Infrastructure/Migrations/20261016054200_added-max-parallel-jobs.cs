using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addedmaxparalleljobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxBackgroundJobsParallelism",
                schema: "identity",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxBackgroundJobsParallelism",
                schema: "identity",
                table: "Tenants");
        }
    }
}
