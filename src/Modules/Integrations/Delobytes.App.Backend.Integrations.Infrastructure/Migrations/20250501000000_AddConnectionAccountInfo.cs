using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddConnectionAccountInfo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CustomerName",
            schema: "integrations",
            table: "Connections",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LegalName",
            schema: "integrations",
            table: "Connections",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Inn",
            schema: "integrations",
            table: "Connections",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CustomerName",
            schema: "integrations",
            table: "Connections");

        migrationBuilder.DropColumn(
            name: "LegalName",
            schema: "integrations",
            table: "Connections");

        migrationBuilder.DropColumn(
            name: "Inn",
            schema: "integrations",
            table: "Connections");
    }
}
