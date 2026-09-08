using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class IndexMembershipReceiptsForGlobalPruning : Migration
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
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "ClubId", "CreatedAt" });
#pragma warning restore CA1861
    }
}
