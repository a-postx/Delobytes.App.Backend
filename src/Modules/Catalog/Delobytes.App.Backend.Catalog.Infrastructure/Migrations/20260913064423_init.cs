using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

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
                name: "PackagingComponents",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingComponents", x => x.Id);
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
                    LengthCm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    WidthCm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
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
                name: "Suppliers",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Inn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TariffGrids",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TariffType = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffGrids", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkRates",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DailyWage = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelParameterSets",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    AcquiringPercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    SppPercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    SppEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TaxType = table.Column<int>(type: "integer", nullable: false),
                    TaxRatePercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelParameterSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelParameterSets_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "catalog",
                        principalTable: "Channels",
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

            migrationBuilder.CreateTable(
                name: "ProductPackagingComponents",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackagingComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPackagingComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPackagingComponents_PackagingComponents_PackagingCom~",
                        column: x => x.PackagingComponentId,
                        principalSchema: "catalog",
                        principalTable: "PackagingComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPackagingComponents_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductWorkRates",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssemblyRatePerDay = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWorkRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductWorkRates_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackagingComponentPrices",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackagingComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingComponentPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagingComponentPrices_PackagingComponents_PackagingCompo~",
                        column: x => x.PackagingComponentId,
                        principalSchema: "catalog",
                        principalTable: "PackagingComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackagingComponentPrices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "catalog",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffGridEntries",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffGridId = table.Column<Guid>(type: "uuid", nullable: false),
                    RegionOrCity = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VolumeThresholdLiters = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffGridEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TariffGridEntries_TariffGrids_TariffGridId",
                        column: x => x.TariffGridId,
                        principalSchema: "catalog",
                        principalTable: "TariffGrids",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductChannelInputs",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelParameterSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawMaterialCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LogisticsToCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PriceWithoutDiscount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductChannelInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductChannelInputs_ChannelParameterSets_ChannelParameterS~",
                        column: x => x.ChannelParameterSetId,
                        principalSchema: "catalog",
                        principalTable: "ChannelParameterSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductChannelInputs_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarginCalculationSnapshots",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductChannelInputId = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffGridId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkRateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RawMaterialCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagingCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PackagingWorkCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LogisticsToMarketplaceCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BuyerPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AcquiringAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NetRevenue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Margin = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MarginPercent = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarginCalculationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarginCalculationSnapshots_ProductChannelInputs_ProductChan~",
                        column: x => x.ProductChannelInputId,
                        principalSchema: "catalog",
                        principalTable: "ProductChannelInputs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelParameterSets_ChannelId_ValidFrom",
                schema: "catalog",
                table: "ChannelParameterSets",
                columns: new[] { "ChannelId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelParameterSets_TenantId",
                schema: "catalog",
                table: "ChannelParameterSets",
                column: "TenantId");

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
                name: "IX_MarginCalculationSnapshots_CalculatedAt",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "CalculatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_ProductChannelInputId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "ProductChannelInputId");

            migrationBuilder.CreateIndex(
                name: "IX_MarginCalculationSnapshots_TenantId",
                schema: "catalog",
                table: "MarginCalculationSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponentPrices_IsActive",
                schema: "catalog",
                table: "PackagingComponentPrices",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponentPrices_PackagingComponentId",
                schema: "catalog",
                table: "PackagingComponentPrices",
                column: "PackagingComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponentPrices_SupplierId",
                schema: "catalog",
                table: "PackagingComponentPrices",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponentPrices_TenantId",
                schema: "catalog",
                table: "PackagingComponentPrices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponentPrices_TenantId_ComponentId_ValidFrom",
                schema: "catalog",
                table: "PackagingComponentPrices",
                columns: new[] { "TenantId", "PackagingComponentId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponents_IsActive",
                schema: "catalog",
                table: "PackagingComponents",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PackagingComponents_TenantId",
                schema: "catalog",
                table: "PackagingComponents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_ChannelParameterSetId",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "ChannelParameterSetId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_ProductId_ChannelParameterSetId_ValidF~",
                schema: "catalog",
                table: "ProductChannelInputs",
                columns: new[] { "ProductId", "ChannelParameterSetId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductChannelInputs_TenantId",
                schema: "catalog",
                table: "ProductChannelInputs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackagingComponents_PackagingComponentId",
                schema: "catalog",
                table: "ProductPackagingComponents",
                column: "PackagingComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackagingComponents_ProductId_PackagingComponentId",
                schema: "catalog",
                table: "ProductPackagingComponents",
                columns: new[] { "ProductId", "PackagingComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPackagingComponents_TenantId",
                schema: "catalog",
                table: "ProductPackagingComponents",
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

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_IsActive",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_ProductId_ValidFrom",
                schema: "catalog",
                table: "ProductWorkRates",
                columns: new[] { "ProductId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductWorkRates_TenantId",
                schema: "catalog",
                table: "ProductWorkRates",
                column: "TenantId");

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

            migrationBuilder.CreateIndex(
                name: "IX_TariffGridEntries_TariffGridId_RegionOrCity_VolumeThreshold~",
                schema: "catalog",
                table: "TariffGridEntries",
                columns: new[] { "TariffGridId", "RegionOrCity", "VolumeThresholdLiters" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffGridEntries_TenantId",
                schema: "catalog",
                table: "TariffGridEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffGrids_IsActive",
                schema: "catalog",
                table: "TariffGrids",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TariffGrids_TariffType",
                schema: "catalog",
                table: "TariffGrids",
                column: "TariffType");

            migrationBuilder.CreateIndex(
                name: "IX_TariffGrids_TenantId",
                schema: "catalog",
                table: "TariffGrids",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TariffGrids_ValidFrom",
                schema: "catalog",
                table: "TariffGrids",
                column: "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRates_IsActive",
                schema: "catalog",
                table: "WorkRates",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRates_TenantId",
                schema: "catalog",
                table: "WorkRates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkRates_ValidFrom",
                schema: "catalog",
                table: "WorkRates",
                column: "ValidFrom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelProducts",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "MarginCalculationSnapshots",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PackagingComponentPrices",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductPackagingComponents",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductWorkRates",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TariffGridEntries",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "WorkRates",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductChannelInputs",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Suppliers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "PackagingComponents",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TariffGrids",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ChannelParameterSets",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Channels",
                schema: "catalog");
        }
    }
}
