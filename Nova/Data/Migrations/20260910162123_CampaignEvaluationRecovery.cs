using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class CampaignEvaluationRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CampaignTagApplicationRemovalReceipts");

        migrationBuilder.DropTable(
            name: "EvaluationNoteMutationReceipts");

        migrationBuilder.AddColumn<Guid>(
            name: "Version",
            table: "Notes",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateTable(
            name: "EvaluationMutationReceipts",
            columns: table => new
            {
                EvaluationMutationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ResultJson = table.Column<string>(type: "text", nullable: false),
                RecoveryExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationMutationReceipts", x => x.EvaluationMutationReceiptId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Notes_ClubId_PlayerCampaignAssignmentId_CreatedAt_NoteId",
            table: "Notes",
            columns: ["ClubId", "PlayerCampaignAssignmentId", "CreatedAt", "NoteId"]);

        migrationBuilder.CreateIndex(
            name: "IX_EvaluationMutationReceipts_ClubId_OperationId",
            table: "EvaluationMutationReceipts",
            columns: ["ClubId", "OperationId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EvaluationMutationReceipts_RecoveryExpiresAt_EvaluationMuta~",
            table: "EvaluationMutationReceipts",
            columns: ["RecoveryExpiresAt", "EvaluationMutationReceiptId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EvaluationMutationReceipts");

        migrationBuilder.DropIndex(
            name: "IX_Notes_ClubId_PlayerCampaignAssignmentId_CreatedAt_NoteId",
            table: "Notes");

        migrationBuilder.DropColumn(
            name: "Version",
            table: "Notes");

        RestorePreviousReceipts(migrationBuilder);
    }

    private static void RestorePreviousReceipts(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CampaignTagApplicationRemovalReceipts",
            columns: table => new
            {
                CampaignTagApplicationRemovalReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CampaignTagApplicationId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true),
                RemovalOperationId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CampaignTagApplicationRemovalReceipts", x => x.CampaignTagApplicationRemovalReceiptId);
                table.ForeignKey(
                    name: "FK_CampaignTagApplicationRemovalReceipts_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        RestorePreviousNoteReceipts(migrationBuilder);
    }

    private static void RestorePreviousNoteReceipts(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EvaluationNoteMutationReceipts",
            columns: table => new
            {
                EvaluationNoteMutationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true),
                MutationType = table.Column<int>(type: "integer", nullable: false),
                NoteId = table.Column<long>(type: "bigint", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationNoteMutationReceipts", x => x.EvaluationNoteMutationReceiptId);
                table.ForeignKey(
                    name: "FK_EvaluationNoteMutationReceipts_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplicationRemovalReceipts_ClubId",
            table: "CampaignTagApplicationRemovalReceipts",
            column: "ClubId");

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplicationRemovalReceipts_RemovalOperationId",
            table: "CampaignTagApplicationRemovalReceipts",
            column: "RemovalOperationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EvaluationNoteMutationReceipts_ClubId",
            table: "EvaluationNoteMutationReceipts",
            column: "ClubId");

        migrationBuilder.CreateIndex(
            name: "IX_EvaluationNoteMutationReceipts_OperationId",
            table: "EvaluationNoteMutationReceipts",
            column: "OperationId",
            unique: true);
    }
}
