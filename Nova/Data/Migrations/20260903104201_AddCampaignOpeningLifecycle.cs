using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddCampaignOpeningLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Campaigns_StatusClosureMetadata",
            table: "Campaigns");

        migrationBuilder.AddColumn<int>(
            name: "InitialActiveTeamCount",
            table: "Campaigns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "InitialEnrolledPlayerCount",
            table: "Campaigns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "OpenedAt",
            table: "Campaigns",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "OpenedById",
            table: "Campaigns",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "OpeningOperationId",
            table: "Campaigns",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "SeasonOpeningSequence",
            table: "Campaigns",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Campaigns_ClubId_OpeningOperationId",
            table: "Campaigns",
            columns: new[] { "ClubId", "OpeningOperationId" },
            unique: true,
            filter: "\"OpeningOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Campaigns_ClubId_SeasonId_SeasonOpeningSequence",
            table: "Campaigns",
            columns: new[] { "ClubId", "SeasonId", "SeasonOpeningSequence" },
            unique: true,
            filter: "\"SeasonOpeningSequence\" IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Campaigns_StatusLifecycleMetadata",
            table: "Campaigns",
            sql: "(\"Status\" = 2 AND \"OpeningOperationId\" IS NULL AND \"OpenedAt\" IS NULL AND \"OpenedById\" IS NULL AND \"SeasonOpeningSequence\" IS NULL AND \"InitialEnrolledPlayerCount\" IS NULL AND \"InitialActiveTeamCount\" IS NULL AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"Status\" = 0 AND \"OpeningOperationId\" IS NOT NULL AND \"OpenedAt\" IS NOT NULL AND \"OpenedById\" IS NOT NULL AND \"SeasonOpeningSequence\" IS NOT NULL AND \"InitialEnrolledPlayerCount\" IS NOT NULL AND \"InitialEnrolledPlayerCount\" >= 0 AND \"InitialActiveTeamCount\" IS NOT NULL AND \"InitialActiveTeamCount\" >= 0 AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"Status\" = 1 AND \"OpeningOperationId\" IS NOT NULL AND \"OpenedAt\" IS NOT NULL AND \"OpenedById\" IS NOT NULL AND \"SeasonOpeningSequence\" IS NOT NULL AND \"InitialEnrolledPlayerCount\" IS NOT NULL AND \"InitialEnrolledPlayerCount\" >= 0 AND \"InitialActiveTeamCount\" IS NOT NULL AND \"InitialActiveTeamCount\" >= 0 AND \"ClosedAt\" IS NOT NULL AND \"ClosedById\" IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Campaigns_ClubId_OpeningOperationId",
            table: "Campaigns");

        migrationBuilder.DropIndex(
            name: "IX_Campaigns_ClubId_SeasonId_SeasonOpeningSequence",
            table: "Campaigns");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Campaigns_StatusLifecycleMetadata",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "InitialActiveTeamCount",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "InitialEnrolledPlayerCount",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "OpenedAt",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "OpenedById",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "OpeningOperationId",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "SeasonOpeningSequence",
            table: "Campaigns");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Campaigns_StatusClosureMetadata",
            table: "Campaigns",
            sql: "(\"Status\" IN (0, 2) AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"Status\" = 1 AND \"ClosedAt\" IS NOT NULL AND \"ClosedById\" IS NOT NULL)");
    }
}
