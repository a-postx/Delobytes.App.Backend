using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class Stage8_AddSuppliers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Suppliers",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ContactInfo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Suppliers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_IsActive",
            schema: "catalog",
            table: "Suppliers",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_Suppliers_TenantId",
            schema: "catalog",
            table: "Suppliers",
            column: "TenantId");

        // Drop old free-text Supplier column from PackagingComponents
        migrationBuilder.DropColumn(
            name: "Supplier",
            schema: "catalog",
            table: "PackagingComponents");

        // Add FK column SupplierId
        migrationBuilder.AddColumn<Guid>(
            name: "SupplierId",
            schema: "catalog",
            table: "PackagingComponents",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PackagingComponents_SupplierId",
            schema: "catalog",
            table: "PackagingComponents",
            column: "SupplierId");

        migrationBuilder.AddForeignKey(
            name: "FK_PackagingComponents_Suppliers_SupplierId",
            schema: "catalog",
            table: "PackagingComponents",
            column: "SupplierId",
            principalSchema: "catalog",
            principalTable: "Suppliers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PackagingComponents_Suppliers_SupplierId",
            schema: "catalog",
            table: "PackagingComponents");

        migrationBuilder.DropIndex(
            name: "IX_PackagingComponents_SupplierId",
            schema: "catalog",
            table: "PackagingComponents");

        migrationBuilder.DropColumn(
            name: "SupplierId",
            schema: "catalog",
            table: "PackagingComponents");

        migrationBuilder.AddColumn<string>(
            name: "Supplier",
            schema: "catalog",
            table: "PackagingComponents",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.DropTable(
            name: "Suppliers",
            schema: "catalog");
    }
}
