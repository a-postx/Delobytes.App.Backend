using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_BomLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductComponents",
                schema: "catalog");

            migrationBuilder.AddColumn<int>(
                name: "Category",
                schema: "catalog",
                table: "Components",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "BomLines",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomLines_Components_ComponentId",
                        column: x => x.ComponentId,
                        principalSchema: "catalog",
                        principalTable: "Components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BomLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_ComponentId",
                schema: "catalog",
                table: "BomLines",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_ProductId_ComponentId_ValidFrom",
                schema: "catalog",
                table: "BomLines",
                columns: new[] { "ProductId", "ComponentId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_ProductId_IsActive",
                schema: "catalog",
                table: "BomLines",
                columns: new[] { "ProductId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_TenantId",
                schema: "catalog",
                table: "BomLines",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BomLines",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "Category",
                schema: "catalog",
                table: "Components");

            migrationBuilder.CreateTable(
                name: "ProductComponents",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductComponents_Components_ComponentId",
                        column: x => x.ComponentId,
                        principalSchema: "catalog",
                        principalTable: "Components",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductComponents_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductComponents_ComponentId",
                schema: "catalog",
                table: "ProductComponents",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductComponents_ProductId_ComponentId",
                schema: "catalog",
                table: "ProductComponents",
                columns: new[] { "ProductId", "ComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductComponents_TenantId",
                schema: "catalog",
                table: "ProductComponents",
                column: "TenantId");
        }
    }
}
