using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Stage9_ConvertUnitToString : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Unit",
            schema: "catalog",
            table: "PackagingComponents",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "Unit",
            schema: "catalog",
            table: "PackagingComponents",
            type: "integer",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(50)",
            oldMaxLength: 50);
    }
}
