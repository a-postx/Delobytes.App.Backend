using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addedinvitationcreatedby : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "identity",
                table: "Invitations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "identity",
                table: "Invitations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                schema: "identity",
                table: "Invitations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "identity",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "identity",
                table: "Invitations");

            migrationBuilder.DropColumn(
                name: "UpdatedByUserId",
                schema: "identity",
                table: "Invitations");
        }
    }
}
