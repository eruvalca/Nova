using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class PreserveMembershipReceiptsAfterClubDeletion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ClubMembershipMutationReceipts_Clubs_ClubId",
            table: "ClubMembershipMutationReceipts");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddForeignKey(
            name: "FK_ClubMembershipMutationReceipts_Clubs_ClubId",
            table: "ClubMembershipMutationReceipts",
            column: "ClubId",
            principalTable: "Clubs",
            principalColumn: "ClubId",
            onDelete: ReferentialAction.Cascade);
    }
}
