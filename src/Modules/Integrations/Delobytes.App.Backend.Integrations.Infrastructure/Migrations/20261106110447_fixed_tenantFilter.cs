using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Integrations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class fixed_tenantFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "integrations",
                table: "ProductsImportJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                schema: "integrations",
                table: "JobCheckpoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ProductsImportJobs_TenantId",
                schema: "integrations",
                table: "ProductsImportJobs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCheckpoints_TenantId",
                schema: "integrations",
                table: "JobCheckpoints",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductsImportJobs_TenantId",
                schema: "integrations",
                table: "ProductsImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_JobCheckpoints_TenantId",
                schema: "integrations",
                table: "JobCheckpoints");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "integrations",
                table: "ProductsImportJobs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                schema: "integrations",
                table: "JobCheckpoints");
        }
    }
}
