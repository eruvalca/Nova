using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddClubCreationOperationId : Migration
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
            columns: new[] { "CreatedById", "CreationOperationId" },
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
