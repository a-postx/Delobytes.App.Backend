using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class add_WorkrateVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkRates_ValidFrom",
                schema: "catalog",
                table: "WorkRates");

            migrationBuilder.DropColumn(
                name: "DailyWage",
                schema: "catalog",
                table: "WorkRates");

            migrationBuilder.DropColumn(
                name: "ValidFrom",
                schema: "catalog",
                table: "WorkRates");

            migrationBuilder.CreateTable(
                name: "WorkRateVersions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkRateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DailyWage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
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
                    table.PrimaryKey("PK_WorkRateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkRateVersions_WorkRates_WorkRateId",
                        column: x => x.WorkRateId,
                        principalSchema: "catalog",
                        principalTable: "WorkRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRateVersions_IsActive",
                schema: "catalog",
                table: "WorkRateVersions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRateVersions_TenantId",
                schema: "catalog",
                table: "WorkRateVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRateVersions_TenantId_WorkRateId_ValidFrom",
                schema: "catalog",
                table: "WorkRateVersions",
                columns: new[] { "TenantId", "WorkRateId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkRateVersions_WorkRateId",
                schema: "catalog",
                table: "WorkRateVersions",
                column: "WorkRateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkRateVersions",
                schema: "catalog");

            migrationBuilder.AddColumn<decimal>(
                name: "DailyWage",
                schema: "catalog",
                table: "WorkRates",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ValidFrom",
                schema: "catalog",
                table: "WorkRates",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateIndex(
                name: "IX_WorkRates_ValidFrom",
                schema: "catalog",
                table: "WorkRates",
                column: "ValidFrom");
        }
    }
}
