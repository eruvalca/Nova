using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddPlayerCreationIdempotency : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreationOperationId",
            table: "Players",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Players_ClubId_CreationOperationId",
            table: "Players",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Players_ClubId_CreationOperationId",
            table: "Players");

        migrationBuilder.DropColumn(
            name: "CreationOperationId",
            table: "Players");
    }
}
