using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class TightenCreationOperationIdsAndNormalizedName : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // DESTRUCTIVE DECISION: rows without a creation-operation identifier predate
        // idempotent creation support, and tag rows without a normalized name predate
        // normalization. All current data is local development data with no production
        // value (see AGENTS.md "Repository decisions"), so those rows are deleted rather
        // than backfilled, and the columns become NOT NULL with plain unique indexes.
        migrationBuilder.Sql("DELETE FROM \"Notes\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"CampaignTagApplications\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"Players\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"PlayerTags\" WHERE \"NormalizedName\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"PlayerTags\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"Teams\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"Campaigns\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"Seasons\" WHERE \"CreationOperationId\" IS NULL;");
        migrationBuilder.Sql("DELETE FROM \"Clubs\" WHERE \"CreationOperationId\" IS NULL;");

        migrationBuilder.DropIndex(
            name: "IX_Teams_ClubId_CreationOperationId",
            table: "Teams");

        migrationBuilder.DropIndex(
            name: "IX_Seasons_ClubId_CreationOperationId",
            table: "Seasons");

        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_CreationOperationId",
            table: "PlayerTags");

        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_NormalizedName",
            table: "PlayerTags");

        migrationBuilder.DropIndex(
            name: "IX_Players_ClubId_CreationOperationId",
            table: "Players");

        migrationBuilder.DropIndex(
            name: "IX_Notes_ClubId_CreationOperationId",
            table: "Notes");

        migrationBuilder.DropIndex(
            name: "IX_Clubs_CreatedById_CreationOperationId",
            table: "Clubs");

        migrationBuilder.DropIndex(
            name: "IX_CampaignTagApplications_ClubId_CreationOperationId",
            table: "CampaignTagApplications");

        migrationBuilder.DropIndex(
            name: "IX_Campaigns_ClubId_CreationOperationId",
            table: "Campaigns");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Teams",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Seasons",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "PlayerTags",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "PlayerTags",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Players",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Notes",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Clubs",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "CampaignTagApplications",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Campaigns",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Teams_ClubId_CreationOperationId",
            table: "Teams",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Seasons_ClubId_CreationOperationId",
            table: "Seasons",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_CreationOperationId",
            table: "PlayerTags",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_NormalizedName",
            table: "PlayerTags",
            columns: new[] { "ClubId", "NormalizedName" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Players_ClubId_CreationOperationId",
            table: "Players",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Notes_ClubId_CreationOperationId",
            table: "Notes",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Clubs_CreatedById_CreationOperationId",
            table: "Clubs",
            columns: new[] { "CreatedById", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplications_ClubId_CreationOperationId",
            table: "CampaignTagApplications",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Campaigns_ClubId_CreationOperationId",
            table: "Campaigns",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Teams_ClubId_CreationOperationId",
            table: "Teams");

        migrationBuilder.DropIndex(
            name: "IX_Seasons_ClubId_CreationOperationId",
            table: "Seasons");

        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_CreationOperationId",
            table: "PlayerTags");

        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_NormalizedName",
            table: "PlayerTags");

        migrationBuilder.DropIndex(
            name: "IX_Players_ClubId_CreationOperationId",
            table: "Players");

        migrationBuilder.DropIndex(
            name: "IX_Notes_ClubId_CreationOperationId",
            table: "Notes");

        migrationBuilder.DropIndex(
            name: "IX_Clubs_CreatedById_CreationOperationId",
            table: "Clubs");

        migrationBuilder.DropIndex(
            name: "IX_CampaignTagApplications_ClubId_CreationOperationId",
            table: "CampaignTagApplications");

        migrationBuilder.DropIndex(
            name: "IX_Campaigns_ClubId_CreationOperationId",
            table: "Campaigns");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Teams",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Seasons",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "PlayerTags",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "PlayerTags",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Players",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Notes",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Clubs",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "CampaignTagApplications",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "CreationOperationId",
            table: "Campaigns",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateIndex(
            name: "IX_Teams_ClubId_CreationOperationId",
            table: "Teams",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Seasons_ClubId_CreationOperationId",
            table: "Seasons",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_CreationOperationId",
            table: "PlayerTags",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_NormalizedName",
            table: "PlayerTags",
            columns: new[] { "ClubId", "NormalizedName" },
            unique: true,
            filter: "\"NormalizedName\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Players_ClubId_CreationOperationId",
            table: "Players",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Notes_ClubId_CreationOperationId",
            table: "Notes",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Clubs_CreatedById_CreationOperationId",
            table: "Clubs",
            columns: new[] { "CreatedById", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplications_ClubId_CreationOperationId",
            table: "CampaignTagApplications",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Campaigns_ClubId_CreationOperationId",
            table: "Campaigns",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");
    }
}
