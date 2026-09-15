using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;
/// <inheritdoc />
public partial class PlacementRecoveryAndHistory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AddColumn<long>(
            name: "ActorUserId",
            table: "PlacementMutationReceipts",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RecoveryExpiresAt",
            table: "PlacementMutationReceipts",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

        migrationBuilder.AddColumn<string>(
            name: "RequestSha256",
            table: "PlacementMutationReceipts",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ResultJson",
            table: "PlacementMutationReceipts",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<long>(
            name: "PlayerId",
            table: "ActivityEvents",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlacementMutationReceipts_RecoveryExpiresAt",
            table: "PlacementMutationReceipts",
            column: "RecoveryExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_ClubId_PlayerId_ActivityEventId",
            table: "ActivityEvents",
            columns: ["ClubId", "PlayerId", "ActivityEventId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropIndex(
            name: "IX_PlacementMutationReceipts_RecoveryExpiresAt",
            table: "PlacementMutationReceipts");

        migrationBuilder.DropIndex(
            name: "IX_ActivityEvents_ClubId_PlayerId_ActivityEventId",
            table: "ActivityEvents");

        migrationBuilder.DropColumn(
            name: "ActorUserId",
            table: "PlacementMutationReceipts");

        migrationBuilder.DropColumn(
            name: "RecoveryExpiresAt",
            table: "PlacementMutationReceipts");

        migrationBuilder.DropColumn(
            name: "RequestSha256",
            table: "PlacementMutationReceipts");

        migrationBuilder.DropColumn(
            name: "ResultJson",
            table: "PlacementMutationReceipts");

        migrationBuilder.DropColumn(
            name: "PlayerId",
            table: "ActivityEvents");
    }
}
