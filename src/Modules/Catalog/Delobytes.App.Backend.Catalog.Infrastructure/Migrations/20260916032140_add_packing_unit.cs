using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_packing_unit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeightCm",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                schema: "catalog",
                table: "Products");

            migrationBuilder.AddColumn<Guid>(
                name: "PackingUnitId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeLiters",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PackingUnits",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LengthCm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    WidthCm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    WeightKg = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingUnits_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackingUnits_ProductId",
                schema: "catalog",
                table: "PackingUnits",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingUnits_TenantId",
                schema: "catalog",
                table: "PackingUnits",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackingUnits",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "PackingUnitId",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.DropColumn(
                name: "VolumeLiters",
                schema: "catalog",
                table: "MarginCalculationSnapshots");

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                schema: "catalog",
                table: "Products",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                schema: "catalog",
                table: "Products",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                schema: "catalog",
                table: "Products",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
