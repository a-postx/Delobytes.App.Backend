using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class numbering_sequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NumberingSchemes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntityKind = table.Column<int>(type: "integer", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberingSchemes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NumberingSequences",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Step = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    EndNumber = table.Column<int>(type: "integer", nullable: true),
                    NextNumber = table.Column<int>(type: "integer", nullable: false),
                    WarnOnClose = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    WarnIfNotContinuous = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ManualNumbering = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberingSequences", x => x.Id);
                    table.CheckConstraint("CK_NumberingSequences_Step", "\"Step\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "NumberingSegments",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    ConstantValue = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NumberingSequenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Length = table.Column<int>(type: "integer", nullable: true),
                    SeparatorAfter = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberingSegments", x => x.Id);
                    table.CheckConstraint("CK_NumberingSegments_AutoNumber", "(\"Type\" = 2 AND \"NumberingSequenceId\" IS NOT NULL) OR \"Type\" <> 2");
                    table.CheckConstraint("CK_NumberingSegments_Constant", "(\"Type\" = 1 AND \"ConstantValue\" IS NOT NULL) OR \"Type\" <> 1");
                    table.CheckConstraint("CK_NumberingSegments_Order", "\"Order\" > 0");
                    table.ForeignKey(
                        name: "FK_NumberingSegments_NumberingSchemes_SchemeId",
                        column: x => x.SchemeId,
                        principalSchema: "catalog",
                        principalTable: "NumberingSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NumberingSegments_NumberingSequences_NumberingSequenceId",
                        column: x => x.NumberingSequenceId,
                        principalSchema: "catalog",
                        principalTable: "NumberingSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSchemes_TenantId",
                schema: "catalog",
                table: "NumberingSchemes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSchemes_TenantId_Code",
                schema: "catalog",
                table: "NumberingSchemes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSchemes_TenantId_Kind_Default",
                schema: "catalog",
                table: "NumberingSchemes",
                columns: new[] { "TenantId", "EntityKind" },
                unique: true,
                filter: "\"IsDefault\"");

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSegments_NumberingSequenceId",
                schema: "catalog",
                table: "NumberingSegments",
                column: "NumberingSequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSegments_SchemeId_Order",
                schema: "catalog",
                table: "NumberingSegments",
                columns: new[] { "SchemeId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSegments_TenantId",
                schema: "catalog",
                table: "NumberingSegments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSequences_TenantId",
                schema: "catalog",
                table: "NumberingSequences",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumberingSegments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "NumberingSchemes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "NumberingSequences",
                schema: "catalog");
        }
    }
}
