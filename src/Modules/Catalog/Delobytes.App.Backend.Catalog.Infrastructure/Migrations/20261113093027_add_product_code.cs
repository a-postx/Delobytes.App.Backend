using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the immutable product code and seeds numbering for tenants that already hold products.
    /// </summary>
    /// <remarks>
    /// Three paths are possible and all three are supported:
    /// <list type="number">
    ///   <item><description>
    ///   <c>Products</c> is empty: the column is added and made required, the unique index is
    ///   created, and the numbering seed inserts and updates nothing.
    ///   </description></item>
    ///   <item><description>
    ///   <c>Products</c> holds rows but every tenant already has a usable default product scheme
    ///   (a developer machine that ran a Stage-1 build): codes are backfilled and the existing
    ///   scheme, its sequence and its segments are updated in place.
    ///   </description></item>
    ///   <item><description>
    ///   <c>Products</c> holds rows and a tenant has no default scheme: the standard <c>PRODUCT</c>
    ///   scheme, its two segments and its sequence are created with <c>NextNumber</c> past the
    ///   highest backfilled code.
    ///   </description></item>
    /// </list>
    /// <c>NextNumber</c> is derived as "number of products of the tenant + 1": the backfill numbers
    /// each tenant's products densely from 1 in <c>("CreatedAt", "Id")</c> order, so the count is
    /// the highest issued number. An existing sequence is never lowered, only raised to that value.
    /// The migration fails with an actionable error instead of touching an inactive default scheme
    /// or a default that belongs to another scheme code.
    /// </remarks>
    public partial class add_product_code : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two steps because a single NOT NULL column addition fails on any table holding a row.
            migrationBuilder.AddColumn<string>(
                name: "Code",
                schema: "catalog",
                table: "Products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // ROW_NUMBER is dense per tenant, so the unique index below cannot see a duplicate.
            // greatest(6, ...) keeps zero-padding to six digits without truncating a tenant that
            // holds more than 999999 products: lpad truncates, the allocator never does.
            migrationBuilder.Sql(
                """
                UPDATE catalog."Products" AS p
                SET "Code" = 'P-' || lpad(
                    ranked."Number"::text,
                    greatest(6, length(ranked."Number"::text)),
                    '0')
                FROM (
                    SELECT
                        "Id",
                        ROW_NUMBER() OVER (PARTITION BY "TenantId" ORDER BY "CreatedAt", "Id") AS "Number"
                    FROM catalog."Products"
                ) AS ranked
                WHERE p."Id" = ranked."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "catalog",
                table: "Products",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Code",
                schema: "catalog",
                table: "Products",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            // Seed numbering only for tenants that hold products. The whole block is inside the
            // migration transaction: a RAISE here rolls the backfill and the column back too.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    conflicting RECORD;
                    tenant_row RECORD;
                    scheme_id uuid;
                    sequence_id uuid;
                    next_number integer;
                BEGIN
                    -- Locked policy: at most one default product scheme per tenant, whether active
                    -- or inactive. A backfilled tenant whose default is inactive, or whose default
                    -- is a different scheme code, cannot be seeded without administrative action.
                    SELECT s."TenantId", s."Id", s."Code", s."IsActive"
                    INTO conflicting
                    FROM catalog."NumberingSchemes" AS s
                    WHERE s."EntityKind" = 1
                      AND s."IsDefault"
                      AND (s."Code" <> 'PRODUCT' OR NOT s."IsActive")
                      AND s."TenantId" IN (SELECT "TenantId" FROM catalog."Products")
                    ORDER BY s."TenantId", s."Id"
                    LIMIT 1;

                    IF FOUND THEN
                        RAISE EXCEPTION
                            'Cannot seed product numbering: tenant % has default product scheme "%" (Id=%, IsActive=%). Activate that scheme, or move the default flag to an active PRODUCT scheme, then apply the migration again.',
                            conflicting."TenantId", conflicting."Code", conflicting."Id", conflicting."IsActive";
                    END IF;

                    FOR tenant_row IN
                        SELECT "TenantId", COUNT(*) AS "ProductCount"
                        FROM catalog."Products"
                        GROUP BY "TenantId"
                        ORDER BY "TenantId"
                    LOOP
                        -- Backfilled codes are dense from 1, so the highest issued number is the
                        -- product count and the first free number follows it.
                        next_number := tenant_row."ProductCount" + 1;

                        SELECT s."Id" INTO scheme_id
                        FROM catalog."NumberingSchemes" AS s
                        WHERE s."TenantId" = tenant_row."TenantId"
                          AND s."Code" = 'PRODUCT'
                          AND s."EntityKind" = 1;

                        IF scheme_id IS NULL THEN
                            scheme_id := gen_random_uuid();

                            INSERT INTO catalog."NumberingSchemes"
                                ("Id", "Code", "Name", "EntityKind", "IsDefault", "IsActive", "CreatedAt", "TenantId")
                            VALUES
                                (scheme_id, 'PRODUCT', 'Product code', 1, true, true, now(), tenant_row."TenantId");
                        ELSE
                            UPDATE catalog."NumberingSchemes"
                            SET "Name" = 'Product code',
                                "IsDefault" = true,
                                "IsActive" = true,
                                "UpdatedAt" = now()
                            WHERE "Id" = scheme_id;
                        END IF;

                        SELECT seg."NumberingSequenceId" INTO sequence_id
                        FROM catalog."NumberingSegments" AS seg
                        WHERE seg."SchemeId" = scheme_id
                          AND seg."Order" = 2;

                        IF sequence_id IS NULL THEN
                            sequence_id := gen_random_uuid();

                            INSERT INTO catalog."NumberingSequences"
                                ("Id", "Name", "StartNumber", "Step", "EndNumber", "NextNumber", "WarnOnClose",
                                 "WarnIfNotContinuous", "ManualNumbering", "IsActive", "CreatedAt", "TenantId")
                            VALUES
                                (sequence_id, 'Product code', 1, 1, NULL, next_number, false,
                                 false, false, true, now(), tenant_row."TenantId");
                        ELSE
                            -- Never lower a counter that is already ahead of the backfill.
                            UPDATE catalog."NumberingSequences"
                            SET "NextNumber" = greatest("NextNumber", next_number),
                                "IsActive" = true,
                                "UpdatedAt" = now()
                            WHERE "Id" = sequence_id;
                        END IF;

                        -- Segment 1: the "P-" constant. NumberingSegmentType.Constant = 1.
                        IF EXISTS (
                            SELECT 1 FROM catalog."NumberingSegments"
                            WHERE "SchemeId" = scheme_id AND "Order" = 1)
                        THEN
                            UPDATE catalog."NumberingSegments"
                            SET "Type" = 1,
                                "ConstantValue" = 'P-',
                                "NumberingSequenceId" = NULL,
                                "Length" = NULL,
                                "SeparatorAfter" = NULL
                            WHERE "SchemeId" = scheme_id AND "Order" = 1;
                        ELSE
                            INSERT INTO catalog."NumberingSegments"
                                ("Id", "SchemeId", "Order", "Type", "ConstantValue", "NumberingSequenceId",
                                 "Length", "SeparatorAfter", "TenantId")
                            VALUES
                                (gen_random_uuid(), scheme_id, 1, 1, 'P-', NULL, NULL, NULL, tenant_row."TenantId");
                        END IF;

                        -- Segment 2: the auto number. NumberingSegmentType.AutoNumber = 2.
                        IF EXISTS (
                            SELECT 1 FROM catalog."NumberingSegments"
                            WHERE "SchemeId" = scheme_id AND "Order" = 2)
                        THEN
                            UPDATE catalog."NumberingSegments"
                            SET "Type" = 2,
                                "ConstantValue" = NULL,
                                "NumberingSequenceId" = sequence_id,
                                "Length" = 6,
                                "SeparatorAfter" = NULL
                            WHERE "SchemeId" = scheme_id AND "Order" = 2;
                        ELSE
                            INSERT INTO catalog."NumberingSegments"
                                ("Id", "SchemeId", "Order", "Type", "ConstantValue", "NumberingSequenceId",
                                 "Length", "SeparatorAfter", "TenantId")
                            VALUES
                                (gen_random_uuid(), scheme_id, 2, 2, NULL, sequence_id, 6, NULL, tenant_row."TenantId");
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The seeded numbering rows are left in place: they are harmless configuration, and
            // deleting them could remove a scheme a developer already uses elsewhere.
            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_Code",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Code",
                schema: "catalog",
                table: "Products");
        }
    }
}
