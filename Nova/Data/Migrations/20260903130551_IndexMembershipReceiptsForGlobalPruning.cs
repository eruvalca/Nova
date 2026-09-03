using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class IndexMembershipReceiptsForGlobalPruning : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ClubMembershipMutationReceipts_ClubId_CreatedAt",
            table: "ClubMembershipMutationReceipts");

        migrationBuilder.CreateIndex(
            name: "IX_ClubMembershipMutationReceipts_CreatedAt",
            table: "ClubMembershipMutationReceipts",
            column: "CreatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ClubMembershipMutationReceipts_CreatedAt",
            table: "ClubMembershipMutationReceipts");

        migrationBuilder.CreateIndex(
            name: "IX_ClubMembershipMutationReceipts_ClubId_CreatedAt",
            table: "ClubMembershipMutationReceipts",
            columns: new[] { "ClubId", "CreatedAt" });
    }
}
