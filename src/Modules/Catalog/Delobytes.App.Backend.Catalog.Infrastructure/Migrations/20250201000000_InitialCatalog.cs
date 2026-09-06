using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.CreateTable(
            name: "Components",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PurchasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Components", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "LaborRates",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                RatePeriod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IncludesTaxes = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LaborRates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Products",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Products", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Channels",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SystemChannelTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CustomApiUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                IsCustom = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Channels", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProductComponents",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

        migrationBuilder.CreateTable(
            name: "ProductLaborCosts",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                LaborRateId = table.Column<Guid>(type: "uuid", nullable: false),
                UnitsProducedPerPeriod = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductLaborCosts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductLaborCosts_LaborRates_LaborRateId",
                    column: x => x.LaborRateId,
                    principalSchema: "catalog",
                    principalTable: "LaborRates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ProductLaborCosts_Products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChannelProducts",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalProductId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ExternalSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ChannelSpecificData = table.Column<string>(type: "text", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                LastSyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChannelProducts", x => x.Id);
                table.ForeignKey(
                    name: "FK_ChannelProducts_Channels_ChannelId",
                    column: x => x.ChannelId,
                    principalSchema: "catalog",
                    principalTable: "Channels",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ChannelProducts_Products_ProductId",
                    column: x => x.ProductId,
                    principalSchema: "catalog",
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ChannelProducts_ChannelId",
            schema: "catalog",
            table: "ChannelProducts",
            column: "ChannelId");

        migrationBuilder.CreateIndex(
            name: "IX_ChannelProducts_ExternalProductId",
            schema: "catalog",
            table: "ChannelProducts",
            column: "ExternalProductId");

        migrationBuilder.CreateIndex(
            name: "IX_ChannelProducts_IsActive",
            schema: "catalog",
            table: "ChannelProducts",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_ChannelProducts_LastSyncedAt",
            schema: "catalog",
            table: "ChannelProducts",
            column: "LastSyncedAt");

        migrationBuilder.CreateIndex(
            name: "IX_ChannelProducts_ProductId_ChannelId",
            schema: "catalog",
            table: "ChannelProducts",
            columns: new[] { "ProductId", "ChannelId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ChannelProducts_TenantId",
            schema: "catalog",
            table: "ChannelProducts",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_Channels_IsActive",
            schema: "catalog",
            table: "Channels",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_Channels_IsCustom",
            schema: "catalog",
            table: "Channels",
            column: "IsCustom");

        migrationBuilder.CreateIndex(
            name: "IX_Channels_SystemChannelTemplateId",
            schema: "catalog",
            table: "Channels",
            column: "SystemChannelTemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_Channels_TenantId",
            schema: "catalog",
            table: "Channels",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_Components_IsActive",
            schema: "catalog",
            table: "Components",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_Components_TenantId",
            schema: "catalog",
            table: "Components",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_LaborRates_IsActive",
            schema: "catalog",
            table: "LaborRates",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_LaborRates_TenantId",
            schema: "catalog",
            table: "LaborRates",
            column: "TenantId");

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

        migrationBuilder.CreateIndex(
            name: "IX_ProductLaborCosts_LaborRateId",
            schema: "catalog",
            table: "ProductLaborCosts",
            column: "LaborRateId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductLaborCosts_ProductId_LaborRateId",
            schema: "catalog",
            table: "ProductLaborCosts",
            columns: new[] { "ProductId", "LaborRateId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductLaborCosts_TenantId",
            schema: "catalog",
            table: "ProductLaborCosts",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_Products_IsActive",
            schema: "catalog",
            table: "Products",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_Products_Sku",
            schema: "catalog",
            table: "Products",
            column: "Sku",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Products_TenantId",
            schema: "catalog",
            table: "Products",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ChannelProducts",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "ProductComponents",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "ProductLaborCosts",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Channels",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Components",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "LaborRates",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Products",
            schema: "catalog");
    }
}
