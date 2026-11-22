using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Finance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class added_PnlFeedState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PnlFeedStates",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastJobRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastBatchAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastChangeAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastBatchAppended = table.Column<int>(type: "integer", nullable: false),
                    LastBatchDuplicates = table.Column<int>(type: "integer", nullable: false),
                    LastBatchFailed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PnlFeedStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PnlFeedStates_TenantId",
                schema: "finance",
                table: "PnlFeedStates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PnlFeedStates_TenantId_ChannelId",
                schema: "finance",
                table: "PnlFeedStates",
                columns: new[] { "TenantId", "ChannelId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PnlFeedStates",
                schema: "finance");
        }
    }
}
