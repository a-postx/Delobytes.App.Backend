using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class renamedproperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LegalName",
                schema: "integrations",
                table: "Connections",
                newName: "CustomerLegalName");

            migrationBuilder.RenameColumn(
                name: "Inn",
                schema: "integrations",
                table: "Connections",
                newName: "CustomerInn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CustomerLegalName",
                schema: "integrations",
                table: "Connections",
                newName: "LegalName");

            migrationBuilder.RenameColumn(
                name: "CustomerInn",
                schema: "integrations",
                table: "Connections",
                newName: "Inn");
        }
    }
}
