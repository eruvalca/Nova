using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddCurrentSeasonFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ConcurrencyToken",
            table: "Seasons",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Seasons"
            SET "ConcurrencyToken" = gen_random_uuid();
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "ConcurrencyToken",
            table: "Seasons",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AddColumn<long>(
            name: "CurrentSeasonId",
            table: "Clubs",
            type: "bigint",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "Clubs" AS club
            SET "CurrentSeasonId" = latest."SeasonId"
            FROM (
                SELECT DISTINCT ON (season."ClubId")
                    season."ClubId",
                    season."SeasonId"
                FROM "Seasons" AS season
                ORDER BY season."ClubId", season."StartDate" DESC, season."SeasonId" DESC
            ) AS latest
            WHERE club."ClubId" = latest."ClubId";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Clubs_CurrentSeasonId_ClubId",
            table: "Clubs",
            columns: new[] { "CurrentSeasonId", "ClubId" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Clubs_Seasons_CurrentSeasonId_ClubId",
            table: "Clubs",
            columns: new[] { "CurrentSeasonId", "ClubId" },
            principalTable: "Seasons",
            principalColumns: new[] { "SeasonId", "ClubId" },
            onDelete: ReferentialAction.NoAction);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Clubs_Seasons_CurrentSeasonId_ClubId",
            table: "Clubs");

        migrationBuilder.DropIndex(
            name: "IX_Clubs_CurrentSeasonId_ClubId",
            table: "Clubs");

        migrationBuilder.DropColumn(
            name: "ConcurrencyToken",
            table: "Seasons");

        migrationBuilder.DropColumn(
            name: "CurrentSeasonId",
            table: "Clubs");

    }
}
