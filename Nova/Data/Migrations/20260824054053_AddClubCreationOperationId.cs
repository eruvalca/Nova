using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class AddClubCreationOperationId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreationOperationId",
            table: "Clubs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Clubs_CreatedById_CreationOperationId",
            table: "Clubs",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "CreatedById", "CreationOperationId" },
#pragma warning restore CA1861
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Clubs_CreatedById_CreationOperationId",
            table: "Clubs");

        migrationBuilder.DropColumn(
            name: "CreationOperationId",
            table: "Clubs");
    }
}
