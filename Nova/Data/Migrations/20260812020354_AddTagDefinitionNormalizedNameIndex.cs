using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagDefinitionNormalizedNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId",
                table: "PlayerTags");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "PlayerTags",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
-- Canonical winner per (ClubId, normalized name) is the lowest PlayerTagId.
WITH group_min AS (
    SELECT ""ClubId"",
           UPPER(TRIM(""Name"")) AS normalized_name,
           MIN(""PlayerTagId"") AS winner_id
    FROM ""PlayerTags""
    GROUP BY ""ClubId"", UPPER(TRIM(""Name""))
)
UPDATE ""CampaignTagApplications"" a
SET ""PlayerTagId"" = g.winner_id
FROM ""PlayerTags"" t
JOIN group_min g
  ON g.""ClubId"" = t.""ClubId""
 AND g.normalized_name = UPPER(TRIM(t.""Name""))
WHERE a.""PlayerTagId"" = t.""PlayerTagId""
  AND t.""PlayerTagId"" <> g.winner_id;

DELETE FROM ""PlayerTags"" t
WHERE t.""PlayerTagId"" IN (
    SELECT t2.""PlayerTagId""
    FROM ""PlayerTags"" t2
    JOIN ""PlayerTags"" winner
      ON winner.""ClubId"" = t2.""ClubId""
     AND UPPER(TRIM(winner.""Name"")) = UPPER(TRIM(t2.""Name""))
     AND winner.""PlayerTagId"" < t2.""PlayerTagId""
);

UPDATE ""PlayerTags"" SET ""NormalizedName"" = UPPER(TRIM(""Name""));
");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId_NormalizedName",
                table: "PlayerTags",
                columns: new[] { "ClubId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId_NormalizedName",
                table: "PlayerTags");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "PlayerTags");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId",
                table: "PlayerTags",
                column: "ClubId");
        }
    }
}
