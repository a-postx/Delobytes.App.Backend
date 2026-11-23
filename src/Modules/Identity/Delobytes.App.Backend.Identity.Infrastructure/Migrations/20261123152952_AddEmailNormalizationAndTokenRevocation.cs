using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailNormalizationAndTokenRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                schema: "identity",
                table: "Users");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                schema: "identity",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TokensValidAfter",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill existing rows; the application sets this column on every new user.
            migrationBuilder.Sql(
                "UPDATE identity.\"Users\" SET \"NormalizedEmail\" = lower(btrim(\"Email\")) WHERE \"NormalizedEmail\" = '';");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedEmail_IdentityProvider",
                schema: "identity",
                table: "Users",
                columns: new[] { "NormalizedEmail", "IdentityProvider" },
                unique: true,
                filter: "\"NormalizedEmail\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedEmail_IdentityProvider",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TokensValidAfter",
                schema: "identity",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "identity",
                table: "Users",
                column: "Email");
        }
    }
}
