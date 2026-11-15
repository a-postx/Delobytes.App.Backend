using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class split_channel_photos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelProductPhotos",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    SizeVariant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelProductPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelProductPhotos_ChannelProducts_ChannelProductId",
                        column: x => x.ChannelProductId,
                        principalSchema: "catalog",
                        principalTable: "ChannelProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The split cannot be corrected afterwards, so the rule is explicit: inside one slot
            // (TenantId, ProductId, DisplayOrder, SizeVariant) the row closest to product creation
            // stays canonical. A Guid.Empty row is canonical by definition and outranks any channel
            // row, whatever its CreatedAt. Every other row becomes the channel photo of its own
            // link; a row whose channel no longer has a link is an orphan and is deleted, so its
            // blob can be collected.
            migrationBuilder.Sql(
                @"WITH ranked AS (
                    SELECT
                        p.""Id"",
                        p.""TenantId"",
                        p.""ProductId"",
                        p.""ChannelId"",
                        p.""DisplayOrder"",
                        p.""SizeVariant"",
                        p.""StorageKey"",
                        p.""ContentHash"",
                        p.""ContentType"",
                        p.""SizeBytes"",
                        p.""Width"",
                        p.""Height"",
                        p.""Source"",
                        p.""Status"",
                        p.""ErrorMessage"",
                        p.""LastCheckedAt"",
                        p.""RemovedAt"",
                        p.""CreatedAt"",
                        p.""UpdatedAt"",
                        p.""UpdatedByUserId"",
                        ROW_NUMBER() OVER (
                            PARTITION BY p.""TenantId"", p.""ProductId"", p.""DisplayOrder"", p.""SizeVariant""
                            ORDER BY
                                (CASE WHEN p.""ChannelId"" = '00000000-0000-0000-0000-000000000000' THEN 0 ELSE 1 END),
                                p.""CreatedAt"",
                                p.""Id"") AS rn
                    FROM catalog.""ProductPhotos"" p
                )
                INSERT INTO catalog.""ChannelProductPhotos"" (
                    ""Id"", ""ChannelProductId"", ""ExternalId"", ""DisplayOrder"", ""SizeVariant"",
                    ""StorageKey"", ""ContentHash"", ""ContentType"", ""SizeBytes"", ""Width"", ""Height"",
                    ""Source"", ""Status"", ""ErrorMessage"", ""LastCheckedAt"", ""RemovedAt"",
                    ""CreatedAt"", ""UpdatedAt"", ""UpdatedByUserId"", ""TenantId"")
                SELECT
                    gen_random_uuid(),
                    cp.""Id"",
                    r.""DisplayOrder"" || '_' || r.""SizeVariant"",
                    r.""DisplayOrder"",
                    r.""SizeVariant"",
                    r.""StorageKey"",
                    r.""ContentHash"",
                    r.""ContentType"",
                    r.""SizeBytes"",
                    r.""Width"",
                    r.""Height"",
                    r.""Source"",
                    r.""Status"",
                    r.""ErrorMessage"",
                    r.""LastCheckedAt"",
                    r.""RemovedAt"",
                    r.""CreatedAt"",
                    r.""UpdatedAt"",
                    r.""UpdatedByUserId"",
                    r.""TenantId""
                FROM ranked r
                JOIN catalog.""ChannelProducts"" cp
                    ON cp.""ProductId"" = r.""ProductId"" AND cp.""ChannelId"" = r.""ChannelId""
                WHERE r.rn > 1
                    AND r.""ChannelId"" <> '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.Sql(
                @"WITH ranked AS (
                    SELECT
                        p.""Id"",
                        p.""TenantId"",
                        p.""ProductId"",
                        p.""DisplayOrder"",
                        p.""SizeVariant"",
                        p.""ChannelId"",
                        ROW_NUMBER() OVER (
                            PARTITION BY p.""TenantId"", p.""ProductId"", p.""DisplayOrder"", p.""SizeVariant""
                            ORDER BY
                                (CASE WHEN p.""ChannelId"" = '00000000-0000-0000-0000-000000000000' THEN 0 ELSE 1 END),
                                p.""CreatedAt"",
                                p.""Id"") AS rn
                    FROM catalog.""ProductPhotos"" p
                )
                DELETE FROM catalog.""ProductPhotos"" p
                USING ranked r
                WHERE p.""Id"" = r.""Id""
                    AND r.rn > 1
                    AND r.""ChannelId"" <> '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_ProductId_DisplayOrder_SizeVariant",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_ProductId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ChannelId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropColumn(
                name: "ChannelId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_ProductId",
                schema: "catalog",
                table: "ProductPhotos",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_DisplayOrder_SizeVariant",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "TenantId", "ProductId", "DisplayOrder", "SizeVariant" },
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductPhotos_ChannelProductId",
                schema: "catalog",
                table: "ChannelProductPhotos",
                column: "ChannelProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductPhotos_TenantId",
                schema: "catalog",
                table: "ChannelProductPhotos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductPhotos_TenantId_ChannelProductId_ExternalId",
                schema: "catalog",
                table: "ChannelProductPhotos",
                columns: new[] { "TenantId", "ChannelProductId", "ExternalId" },
                unique: true,
                filter: "\"Status\" <> 3");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductPhotos_TenantId_ContentHash",
                schema: "catalog",
                table: "ChannelProductPhotos",
                columns: new[] { "TenantId", "ContentHash" });
        }

        /// <inheritdoc />
        /// <remarks>
        /// The backfill is not reversible: channel photos are dropped together with the table, and
        /// the canonical rows deleted by the grouping rule cannot be reconstructed. The restored
        /// ChannelId column is therefore empty for every row, which is the documented state of a
        /// legacy row, not a faithful rollback.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelProductPhotos",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_ProductId",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.DropIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_DisplayOrder_SizeVariant",
                schema: "catalog",
                table: "ProductPhotos");

            migrationBuilder.AddColumn<Guid>(
                name: "ChannelId",
                schema: "catalog",
                table: "ProductPhotos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_ProductId_DisplayOrder_SizeVariant",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "ProductId", "DisplayOrder", "SizeVariant" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_ProductId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "ProductId", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPhotos_TenantId_ProductId_ChannelId_ExternalId",
                schema: "catalog",
                table: "ProductPhotos",
                columns: new[] { "TenantId", "ProductId", "ChannelId", "ExternalId" },
                unique: true,
                filter: "\"Status\" <> 3");
        }
    }
}
