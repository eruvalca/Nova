using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddClubActivityEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClubActivityEvents",
            columns: table => new
            {
                ClubActivityEventId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                EventKind = table.Column<int>(type: "integer", nullable: false),
                Audience = table.Column<int>(type: "integer", nullable: false),
                ActorDisplayName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: false),
                SubjectUserId = table.Column<long>(type: "bigint", nullable: true),
                SubjectDisplayName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: true),
                JoinRequestId = table.Column<long>(type: "bigint", nullable: true),
                CampaignId = table.Column<long>(type: "bigint", nullable: true),
                CampaignName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SeasonName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PlayerCampaignAssignmentId = table.Column<long>(type: "bigint", nullable: true),
                PlayerId = table.Column<long>(type: "bigint", nullable: true),
                PlayerDisplayName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: true),
                PreviousPlacementOutcome = table.Column<int>(type: "integer", nullable: true),
                PreviousTeamId = table.Column<long>(type: "bigint", nullable: true),
                PreviousTeamName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PreviousSourceCampaignName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CurrentPlacementOutcome = table.Column<int>(type: "integer", nullable: true),
                CurrentTeamId = table.Column<long>(type: "bigint", nullable: true),
                CurrentTeamName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CurrentSourceCampaignName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClubActivityEvents", x => x.ClubActivityEventId);
                table.CheckConstraint("CK_ClubActivityEvents_Audience", "\"Audience\" IN (0, 1)");
                table.CheckConstraint("CK_ClubActivityEvents_EventKind", "\"EventKind\" BETWEEN 0 AND 16");
                table.ForeignKey(
                    name: "FK_ClubActivityEvents_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ClubActivityEvents_ClubId_Audience_CreatedAt_ClubActivityEv~",
            table: "ClubActivityEvents",
            columns: new[] { "ClubId", "Audience", "CreatedAt", "ClubActivityEventId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ClubActivityEvents");
    }
}
