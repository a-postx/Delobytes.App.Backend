using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddIsActiveToRawMaterialRates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            schema: "catalog",
            table: "RawMaterialRates",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            schema: "catalog",
            table: "RawMaterialRates",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RawMaterialRates_IsActive",
            schema: "catalog",
            table: "RawMaterialRates",
            column: "IsActive");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_RawMaterialRates_IsActive",
            schema: "catalog",
            table: "RawMaterialRates");

        migrationBuilder.DropColumn(
            name: "IsActive",
            schema: "catalog",
            table: "RawMaterialRates");

        migrationBuilder.DropColumn(
            name: "UpdatedAt",
            schema: "catalog",
            table: "RawMaterialRates");
    }
}
