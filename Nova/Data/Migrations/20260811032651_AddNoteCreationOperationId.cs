using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddNoteCreationOperationId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Notes_ClubId",
            table: "Notes");

        migrationBuilder.AddColumn<Guid>(
            name: "CreationOperationId",
            table: "Notes",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Notes_ClubId_CreationOperationId",
            table: "Notes",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Notes_ClubId_CreationOperationId",
            table: "Notes");

        migrationBuilder.DropColumn(
            name: "CreationOperationId",
            table: "Notes");

        migrationBuilder.CreateIndex(
            name: "IX_Notes_ClubId",
            table: "Notes",
            column: "ClubId");
    }
}
