using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignParticipationIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerCampaignAssignments_CampaignId",
                table: "PlayerCampaignAssignments");

            migrationBuilder.DropColumn(
                name: "TryoutNumber",
                table: "Players");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyToken",
                table: "PlayerCampaignAssignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "PlacementOutcome",
                table: "PlayerCampaignAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TryoutNumber",
                table: "PlayerCampaignAssignments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCampaignAssignments_CampaignId_PlayerId",
                table: "PlayerCampaignAssignments",
                columns: new[] { "CampaignId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCampaignAssignments_CampaignId_TryoutNumber",
                table: "PlayerCampaignAssignments",
                columns: new[] { "CampaignId", "TryoutNumber" },
                unique: true,
                filter: "\"TryoutNumber\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlayerCampaignAssignments_PlacementOutcomeTeam",
                table: "PlayerCampaignAssignments",
                sql: "(\"PlacementOutcome\" = 1 AND \"TeamId\" IS NOT NULL) OR (\"PlacementOutcome\" IN (0, 2, 3) AND \"TeamId\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerCampaignAssignments_CampaignId_PlayerId",
                table: "PlayerCampaignAssignments");

            migrationBuilder.DropIndex(
                name: "IX_PlayerCampaignAssignments_CampaignId_TryoutNumber",
                table: "PlayerCampaignAssignments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlayerCampaignAssignments_PlacementOutcomeTeam",
                table: "PlayerCampaignAssignments");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "PlayerCampaignAssignments");

            migrationBuilder.DropColumn(
                name: "PlacementOutcome",
                table: "PlayerCampaignAssignments");

            migrationBuilder.DropColumn(
                name: "TryoutNumber",
                table: "PlayerCampaignAssignments");

            migrationBuilder.AddColumn<int>(
                name: "TryoutNumber",
                table: "Players",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCampaignAssignments_CampaignId",
                table: "PlayerCampaignAssignments",
                column: "CampaignId");
        }
    }
}
