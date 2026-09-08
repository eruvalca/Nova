using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class AddCurrentSeasonFoundation : Migration
{
    /// <inheritdoc />
#pragma warning disable MA0051 // Keep the generated migration operations ordered within their Up/Down method.
    protected override void Up(MigrationBuilder migrationBuilder)
#pragma warning restore MA0051
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

        migrationBuilder.AddColumn<int>(
            name: "CreationKind",
            table: "Seasons",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<long>(
            name: "CreationPreviousSeasonId",
            table: "Seasons",
            type: "bigint",
            nullable: true);

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
            name: "IX_Seasons_CreationPreviousSeasonId_ClubId",
            table: "Seasons",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "CreationPreviousSeasonId", "ClubId" });
#pragma warning restore CA1861

        migrationBuilder.CreateIndex(
            name: "IX_Clubs_CurrentSeasonId_ClubId",
            table: "Clubs",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "CurrentSeasonId", "ClubId" },
#pragma warning restore CA1861
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Clubs_Seasons_CurrentSeasonId_ClubId",
            table: "Clubs",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "CurrentSeasonId", "ClubId" },
#pragma warning restore CA1861
            principalTable: "Seasons",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            principalColumns: new[] { "SeasonId", "ClubId" },
#pragma warning restore CA1861
            onDelete: ReferentialAction.NoAction);

        migrationBuilder.AddForeignKey(
            name: "FK_Seasons_Seasons_CreationPreviousSeasonId_ClubId",
            table: "Seasons",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "CreationPreviousSeasonId", "ClubId" },
#pragma warning restore CA1861
            principalTable: "Seasons",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            principalColumns: new[] { "SeasonId", "ClubId" },
#pragma warning restore CA1861
            onDelete: ReferentialAction.NoAction);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Clubs_Seasons_CurrentSeasonId_ClubId",
            table: "Clubs");

        migrationBuilder.DropForeignKey(
            name: "FK_Seasons_Seasons_CreationPreviousSeasonId_ClubId",
            table: "Seasons");

        migrationBuilder.DropIndex(
            name: "IX_Seasons_CreationPreviousSeasonId_ClubId",
            table: "Seasons");

        migrationBuilder.DropIndex(
            name: "IX_Clubs_CurrentSeasonId_ClubId",
            table: "Clubs");

        migrationBuilder.DropColumn(
            name: "ConcurrencyToken",
            table: "Seasons");

        migrationBuilder.DropColumn(
            name: "CreationKind",
            table: "Seasons");

        migrationBuilder.DropColumn(
            name: "CreationPreviousSeasonId",
            table: "Seasons");

        migrationBuilder.DropColumn(
            name: "CurrentSeasonId",
            table: "Clubs");
    }
}
