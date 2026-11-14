using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class fixed_barcodesimport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelProductBarcodes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelProductBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelProductBarcodes_ChannelProducts_ChannelProductId",
                        column: x => x.ChannelProductId,
                        principalSchema: "catalog",
                        principalTable: "ChannelProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductBarcodes_ChannelProductId",
                schema: "catalog",
                table: "ChannelProductBarcodes",
                column: "ChannelProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductBarcodes_TenantId",
                schema: "catalog",
                table: "ChannelProductBarcodes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductBarcodes_TenantId_ChannelProductId_Value",
                schema: "catalog",
                table: "ChannelProductBarcodes",
                columns: new[] { "TenantId", "ChannelProductId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductBarcodes_TenantId_Value",
                schema: "catalog",
                table: "ChannelProductBarcodes",
                columns: new[] { "TenantId", "Value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelProductBarcodes",
                schema: "catalog");
        }
    }
}
