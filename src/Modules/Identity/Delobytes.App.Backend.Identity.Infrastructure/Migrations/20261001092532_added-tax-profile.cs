using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addedtaxprofile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                schema: "identity",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TaxType",
                schema: "identity",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "VatType",
                schema: "identity",
                table: "Tenants");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZone",
                schema: "identity",
                table: "Tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                schema: "identity",
                table: "Tenants",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "TenantTaxProfiles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Regime = table.Column<int>(type: "integer", nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Vat = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantTaxProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantTaxProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "identity",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantTaxProfiles_TenantId",
                schema: "identity",
                table: "TenantTaxProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantTaxProfiles_TenantId_ValidFrom",
                schema: "identity",
                table: "TenantTaxProfiles",
                columns: new[] { "TenantId", "ValidFrom" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantTaxProfiles",
                schema: "identity");

            migrationBuilder.AlterColumn<string>(
                name: "TimeZone",
                schema: "identity",
                table: "Tenants",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                schema: "identity",
                table: "Tenants",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3)",
                oldMaxLength: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                schema: "identity",
                table: "Tenants",
                type: "numeric(8,6)",
                precision: 8,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TaxType",
                schema: "identity",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VatType",
                schema: "identity",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
