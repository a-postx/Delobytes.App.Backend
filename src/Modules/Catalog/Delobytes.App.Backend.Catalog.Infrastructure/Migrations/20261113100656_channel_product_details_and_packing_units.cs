using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the channel-owned name and description of a card, and splits packing units into a
    /// canonical row plus one row per channel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filtered unique index <c>IX_PackingUnits_Product_Canonical</c> allows at most one
    /// canonical row per product (<c>ChannelId IS NULL</c>). PostgreSQL treats NULLs as distinct,
    /// so the composite index cannot bound that row. Nothing in the application prevents a race
    /// from producing two canonical rows today: two concurrent saves both find none and both
    /// append one. The migration therefore collapses duplicates before creating the filtered
    /// index; on a database where no such race happened it deletes nothing.
    /// </para>
    /// <para>
    /// Deduplication keeps, per product, the row that is active first, then the most recent
    /// <c>CreatedAt</c>, then the lowest <c>Id</c>, and deletes the rest. Those deleted rows are
    /// gone: the columns carry no history, so a duplicate unit that was not the winner cannot be
    /// restored from this migration. The composite index needs no dedupe because it covers
    /// non-null channel rows only, where the channel id is part of the key.
    /// </para>
    /// <para>
    /// <c>IX_PackingUnits_ProductId</c> is dropped rather than kept: <c>ProductId</c> is the
    /// leading column of the new composite index, which serves every lookup by product. Keeping
    /// both would double the write cost of every insert for no read benefit.
    /// </para>
    /// </remarks>
    public partial class channel_product_details_and_packing_units : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PackingUnits_ProductId",
                schema: "catalog",
                table: "PackingUnits");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "catalog",
                table: "ChannelProducts",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                schema: "catalog",
                table: "ChannelProducts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackingUnits_ChannelId",
                schema: "catalog",
                table: "PackingUnits",
                column: "ChannelId");

            // Hand-written: the model says "at most one canonical row", and this is the statement
            // that makes an existing database satisfy it. Deliberately not an IsActive-only rule:
            // inactive canonical rows compete too, because the filtered index does not look at
            // IsActive. On a clean database this deletes nothing.
            migrationBuilder.Sql(
                """
                DELETE FROM catalog."PackingUnits" AS pu
                WHERE pu."ChannelId" IS NULL
                  AND pu."Id" IN (
                      SELECT ranked."Id"
                      FROM (
                          SELECT
                              candidate."Id",
                              ROW_NUMBER() OVER (
                                  PARTITION BY candidate."ProductId"
                                  ORDER BY candidate."IsActive" DESC, candidate."CreatedAt" DESC, candidate."Id" ASC
                              ) AS "Position"
                          FROM catalog."PackingUnits" AS candidate
                          WHERE candidate."ChannelId" IS NULL
                      ) AS ranked
                      WHERE ranked."Position" > 1
                  );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PackingUnits_Product_Canonical",
                schema: "catalog",
                table: "PackingUnits",
                column: "ProductId",
                unique: true,
                filter: "\"ChannelId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PackingUnits_Product_Channel",
                schema: "catalog",
                table: "PackingUnits",
                columns: new[] { "ProductId", "ChannelId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PackingUnits_Channels_ChannelId",
                schema: "catalog",
                table: "PackingUnits",
                column: "ChannelId",
                principalSchema: "catalog",
                principalTable: "Channels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackingUnits_Channels_ChannelId",
                schema: "catalog",
                table: "PackingUnits");

            migrationBuilder.DropIndex(
                name: "IX_PackingUnits_ChannelId",
                schema: "catalog",
                table: "PackingUnits");

            migrationBuilder.DropIndex(
                name: "IX_PackingUnits_Product_Canonical",
                schema: "catalog",
                table: "PackingUnits");

            migrationBuilder.DropIndex(
                name: "IX_PackingUnits_Product_Channel",
                schema: "catalog",
                table: "PackingUnits");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "catalog",
                table: "ChannelProducts");

            migrationBuilder.DropColumn(
                name: "Name",
                schema: "catalog",
                table: "ChannelProducts");

            migrationBuilder.CreateIndex(
                name: "IX_PackingUnits_ProductId",
                schema: "catalog",
                table: "PackingUnits",
                column: "ProductId");
        }
    }
}
