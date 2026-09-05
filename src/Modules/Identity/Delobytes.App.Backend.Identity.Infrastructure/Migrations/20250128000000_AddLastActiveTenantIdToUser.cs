using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delobytes.App.Backend.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddLastActiveTenantIdToUser : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "LastActiveTenantId",
            schema: "identity",
            table: "Users",
            type: "uuid",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastActiveTenantId",
            schema: "identity",
            table: "Users");
    }
}
