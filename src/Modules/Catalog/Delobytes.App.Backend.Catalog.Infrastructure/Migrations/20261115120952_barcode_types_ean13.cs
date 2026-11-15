using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class barcode_types_ean13 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Unconditional on purpose: every existing value is a channel code written by the old
            // canonical assignment ("wildberries", "ozon") or NULL. None of them is a symbology,
            // so there is no data worth preserving. ChannelProductBarcodes."Type" is not touched:
            // there the channel's own type is the correct value.
            migrationBuilder.Sql(
                "UPDATE catalog.\"ProductBarcodes\" SET \"Type\" = 'EAN13';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: the previous values were channel codes and cannot be reconstructed from the
            // current rows. Restoring a wrong value would be worse than leaving the symbology.
        }
    }
}
