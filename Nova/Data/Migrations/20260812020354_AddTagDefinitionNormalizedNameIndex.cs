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
WITH ranked AS (
    SELECT ""PlayerTagId"",
           ROW_NUMBER() OVER (
               PARTITION BY ""ClubId"", UPPER(TRIM(""Name""))
               ORDER BY ""PlayerTagId"" ASC
           ) AS row_number
    FROM ""PlayerTags""
),
duplicate_tags AS (
    SELECT t.""PlayerTagId"" AS duplicate_id,
           winner.""PlayerTagId"" AS winner_id
    FROM ""PlayerTags"" t
    JOIN ranked r ON r.""PlayerTagId"" = t.""PlayerTagId""
    JOIN ""PlayerTags"" winner
      ON winner.""ClubId"" = t.""ClubId""
     AND UPPER(TRIM(winner.""Name"")) = UPPER(TRIM(t.""Name""))
     AND winner.""PlayerTagId"" < t.""PlayerTagId""
    WHERE r.row_number > 1
)
UPDATE ""CampaignTagApplications"" a
SET ""PlayerTagId"" = d.winner_id
FROM duplicate_tags d
WHERE a.""PlayerTagId"" = d.duplicate_id;

DELETE FROM ""PlayerTags"" t
WHERE t.""PlayerTagId"" IN (
    SELECT duplicate_id
    FROM (
        SELECT DISTINCT d.duplicate_id
        FROM duplicate_tags d
    ) AS duplicates
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
