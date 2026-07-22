using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignTagApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerEntityPlayerTagEntity");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PlayerTags_PlayerTagId_ClubId",
                table: "PlayerTags",
                columns: new[] { "PlayerTagId", "ClubId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PlayerCampaignAssignments_PlayerCampaignAssignmentId_ClubId",
                table: "PlayerCampaignAssignments",
                columns: new[] { "PlayerCampaignAssignmentId", "ClubId" });

            migrationBuilder.CreateTable(
                name: "CampaignTagApplications",
                columns: table => new
                {
                    CampaignTagApplicationId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerCampaignAssignmentId = table.Column<long>(type: "bigint", nullable: false),
                    PlayerTagId = table.Column<long>(type: "bigint", nullable: false),
                    ClubId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<long>(type: "bigint", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignTagApplications", x => x.CampaignTagApplicationId);
                    table.ForeignKey(
                        name: "FK_CampaignTagApplications_Clubs_ClubId",
                        column: x => x.ClubId,
                        principalTable: "Clubs",
                        principalColumn: "ClubId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignTagApplications_PlayerCampaignAssignments_PlayerCam~",
                        columns: x => new { x.PlayerCampaignAssignmentId, x.ClubId },
                        principalTable: "PlayerCampaignAssignments",
                        principalColumns: new[] { "PlayerCampaignAssignmentId", "ClubId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignTagApplications_PlayerTags_PlayerTagId_ClubId",
                        columns: x => new { x.PlayerTagId, x.ClubId },
                        principalTable: "PlayerTags",
                        principalColumns: new[] { "PlayerTagId", "ClubId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTagApplications_ClubId",
                table: "CampaignTagApplications",
                column: "ClubId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTagApplications_PlayerCampaignAssignmentId_ClubId",
                table: "CampaignTagApplications",
                columns: new[] { "PlayerCampaignAssignmentId", "ClubId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTagApplications_PlayerCampaignAssignmentId_PlayerTa~",
                table: "CampaignTagApplications",
                columns: new[] { "PlayerCampaignAssignmentId", "PlayerTagId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignTagApplications_PlayerTagId_ClubId",
                table: "CampaignTagApplications",
                columns: new[] { "PlayerTagId", "ClubId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignTagApplications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PlayerTags_PlayerTagId_ClubId",
                table: "PlayerTags");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PlayerCampaignAssignments_PlayerCampaignAssignmentId_ClubId",
                table: "PlayerCampaignAssignments");

            migrationBuilder.CreateTable(
                name: "PlayerEntityPlayerTagEntity",
                columns: table => new
                {
                    PlayerEntityPlayerId = table.Column<long>(type: "bigint", nullable: false),
                    TagsPlayerTagId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerEntityPlayerTagEntity", x => new { x.PlayerEntityPlayerId, x.TagsPlayerTagId });
                    table.ForeignKey(
                        name: "FK_PlayerEntityPlayerTagEntity_PlayerTags_TagsPlayerTagId",
                        column: x => x.TagsPlayerTagId,
                        principalTable: "PlayerTags",
                        principalColumn: "PlayerTagId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerEntityPlayerTagEntity_Players_PlayerEntityPlayerId",
                        column: x => x.PlayerEntityPlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerEntityPlayerTagEntity_TagsPlayerTagId",
                table: "PlayerEntityPlayerTagEntity",
                column: "TagsPlayerTagId");
        }
    }
}
