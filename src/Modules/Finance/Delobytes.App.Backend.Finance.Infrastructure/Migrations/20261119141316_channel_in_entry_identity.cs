using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class channel_in_entry_identity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PnlEntries_TenantId_Source_ReportInstanceKey_ExternalEntryI~",
                schema: "finance",
                table: "PnlEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId_ChannelId_Source_ReportInstanceKey_Exte~",
                schema: "finance",
                table: "PnlEntries",
                columns: new[] { "TenantId", "ChannelId", "Source", "ReportInstanceKey", "ExternalEntryId", "ExternalProductId", "Kind", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PnlEntries_TenantId_ChannelId_Source_ReportInstanceKey_Exte~",
                schema: "finance",
                table: "PnlEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PnlEntries_TenantId_Source_ReportInstanceKey_ExternalEntryI~",
                schema: "finance",
                table: "PnlEntries",
                columns: new[] { "TenantId", "Source", "ReportInstanceKey", "ExternalEntryId", "ExternalProductId", "Kind", "Type" },
                unique: true);
        }
    }
}
