using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagDefinitionUniquenessAndCreationOperationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId",
                table: "PlayerTags");

            migrationBuilder.AddColumn<Guid>(
                name: "CreationOperationId",
                table: "PlayerTags",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "PlayerTags",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"PlayerTags\" SET \"NormalizedName\" = upper(trim(\"Name\"));");

            // Fail loudly when a club already owns tags that normalize to the same value, because the
            // unique index below cannot be created until those duplicates are resolved. Detecting them
            // here avoids aborting the migration (and blocking deployment) with an opaque duplicate-key
            // error that names no club.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    duplicate_club bigint;
                    duplicate_name text;
                BEGIN
                    SELECT "ClubId", "NormalizedName"
                      INTO duplicate_club, duplicate_name
                      FROM "PlayerTags"
                     WHERE "NormalizedName" IS NOT NULL
                     GROUP BY "ClubId", "NormalizedName"
                    HAVING count(*) > 1
                     LIMIT 1;

                    IF FOUND THEN
                        RAISE EXCEPTION
                            'Cannot create unique index IX_PlayerTags_ClubId_NormalizedName: ClubId=% has multiple tag definitions with the same normalized name (%). Rename or archive the duplicates, then re-run the migration.',
                            duplicate_club, duplicate_name;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId_CreationOperationId",
                table: "PlayerTags",
                columns: new[] { "ClubId", "CreationOperationId" },
                unique: true,
                filter: "\"CreationOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId_LifecycleStatus",
                table: "PlayerTags",
                columns: new[] { "ClubId", "LifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId_NormalizedName",
                table: "PlayerTags",
                columns: new[] { "ClubId", "NormalizedName" },
                unique: true,
                filter: "\"NormalizedName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId_CreationOperationId",
                table: "PlayerTags");

            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId_LifecycleStatus",
                table: "PlayerTags");

            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId_NormalizedName",
                table: "PlayerTags");

            migrationBuilder.DropColumn(
                name: "CreationOperationId",
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
