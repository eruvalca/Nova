using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class PlacementDecisionSupersession : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DecisionActorDisplayName",
            table: "PlayerCampaignAssignments",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DecisionRecordedAt",
            table: "PlayerCampaignAssignments",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "DecisionRecordedById",
            table: "PlayerCampaignAssignments",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_PlayerCampaignAssignments_DecisionAttribution",
            table: "PlayerCampaignAssignments",
            sql: "(\"PlacementOutcome\" = 0 AND \"DecisionRecordedAt\" IS NULL AND \"DecisionRecordedById\" IS NULL AND \"DecisionActorDisplayName\" IS NULL) OR (\"PlacementOutcome\" IN (1, 2, 3) AND \"DecisionRecordedAt\" IS NOT NULL AND \"DecisionRecordedById\" IS NOT NULL AND \"DecisionRecordedById\" > 0 AND \"DecisionActorDisplayName\" IS NOT NULL AND length(trim(\"DecisionActorDisplayName\")) > 0)");

        migrationBuilder.CreateTable(
            name: "PlacementMutationReceipts",
            columns: table => new
            {
                PlacementMutationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerCampaignAssignmentId = table.Column<long>(type: "bigint", nullable: false),
                ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlacementMutationReceipts", x => x.PlacementMutationReceiptId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlacementMutationReceipts_ClubId_CreatedAt",
            table: "PlacementMutationReceipts",
            columns: new[] { "ClubId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_PlacementMutationReceipts_ClubId_OperationId",
            table: "PlacementMutationReceipts",
            columns: new[] { "ClubId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlacementMutationReceipts_CreatedAt",
            table: "PlacementMutationReceipts",
            column: "CreatedAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_PlayerCampaignAssignments_DecisionAttribution", table: "PlayerCampaignAssignments");

        migrationBuilder.DropTable(
            name: "PlacementMutationReceipts");

        migrationBuilder.DropColumn(
            name: "DecisionActorDisplayName",
            table: "PlayerCampaignAssignments");

        migrationBuilder.DropColumn(
            name: "DecisionRecordedAt",
            table: "PlayerCampaignAssignments");

        migrationBuilder.DropColumn(
            name: "DecisionRecordedById",
            table: "PlayerCampaignAssignments");
    }
}
