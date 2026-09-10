using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Stage8_AddSupplierFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add Inn (required, max 12) with a temporary default for existing rows
        migrationBuilder.AddColumn<string>(
            name: "Inn",
            schema: "catalog",
            table: "Suppliers",
            type: "character varying(12)",
            maxLength: 12,
            nullable: false,
            defaultValue: "0000000000");

        migrationBuilder.AddColumn<string>(
            name: "Description",
            schema: "catalog",
            table: "Suppliers",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Phone",
            schema: "catalog",
            table: "Suppliers",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Email",
            schema: "catalog",
            table: "Suppliers",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        // Drop old ContactInfo column (merged into Description/Phone/Email)
        migrationBuilder.DropColumn(
            name: "ContactInfo",
            schema: "catalog",
            table: "Suppliers");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Inn", schema: "catalog", table: "Suppliers");
        migrationBuilder.DropColumn(name: "Description", schema: "catalog", table: "Suppliers");
        migrationBuilder.DropColumn(name: "Phone", schema: "catalog", table: "Suppliers");
        migrationBuilder.DropColumn(name: "Email", schema: "catalog", table: "Suppliers");

        migrationBuilder.AddColumn<string>(
            name: "ContactInfo",
            schema: "catalog",
            table: "Suppliers",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);
    }
}
