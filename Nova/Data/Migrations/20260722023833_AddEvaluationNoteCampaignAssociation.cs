using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationNoteCampaignAssociation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DESTRUCTIVE DECISION: All existing notes referenced players directly and cannot be
            // associated with a valid PlayerCampaignAssignmentId. All current note data is local
            // development data and has no production value. Notes are deleted before renaming the
            // FK column so the new foreign key can be applied cleanly without orphaned rows.
            migrationBuilder.Sql("DELETE FROM \"Notes\";");

            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Players_PlayerId",
                table: "Notes");

            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "Notes",
                newName: "PlayerCampaignAssignmentId");

            migrationBuilder.RenameIndex(
                name: "IX_Notes_PlayerId",
                table: "Notes",
                newName: "IX_Notes_PlayerCampaignAssignmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_PlayerCampaignAssignments_PlayerCampaignAssignmentId",
                table: "Notes",
                column: "PlayerCampaignAssignmentId",
                principalTable: "PlayerCampaignAssignments",
                principalColumn: "PlayerCampaignAssignmentId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_PlayerCampaignAssignments_PlayerCampaignAssignmentId",
                table: "Notes");

            migrationBuilder.RenameColumn(
                name: "PlayerCampaignAssignmentId",
                table: "Notes",
                newName: "PlayerId");

            migrationBuilder.RenameIndex(
                name: "IX_Notes_PlayerCampaignAssignmentId",
                table: "Notes",
                newName: "IX_Notes_PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Players_PlayerId",
                table: "Notes",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "PlayerId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
