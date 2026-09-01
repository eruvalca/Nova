using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddActivityEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CampaignLifecycleEvents");

        migrationBuilder.CreateTable(
            name: "ActivityEvents",
            columns: table => new
            {
                ActivityEventId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                EventKind = table.Column<int>(type: "integer", nullable: false),
                IsAdminOnly = table.Column<bool>(type: "boolean", nullable: false),
                CampaignId = table.Column<long>(type: "bigint", nullable: true),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                ActorDisplayName = table.Column<string>(type: "character varying(201)", maxLength: 201, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ActivityEvents", x => x.ActivityEventId);
                table.ForeignKey(
                    name: "FK_ActivityEvents_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_ClubId_CampaignId",
            table: "ActivityEvents",
            columns: new[] { "ClubId", "CampaignId" });

        migrationBuilder.CreateIndex(
            name: "IX_ActivityEvents_ClubId_CreatedAt_ActivityEventId",
            table: "ActivityEvents",
            columns: new[] { "ClubId", "CreatedAt", "ActivityEventId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ActivityEvents");

        migrationBuilder.CreateTable(
            name: "CampaignLifecycleEvents",
            columns: table => new
            {
                CampaignLifecycleEventId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                CampaignId = table.Column<long>(type: "bigint", nullable: false),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                EventType = table.Column<int>(type: "integer", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CampaignLifecycleEvents", x => x.CampaignLifecycleEventId);
                table.CheckConstraint("CK_CampaignLifecycleEvents_EventType", "\"EventType\" IN (0, 1)");
                table.ForeignKey(
                    name: "FK_CampaignLifecycleEvents_Campaigns_CampaignId_ClubId",
                    columns: x => new { x.CampaignId, x.ClubId },
                    principalTable: "Campaigns",
                    principalColumns: new[] { "CampaignId", "ClubId" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CampaignLifecycleEvents_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CampaignLifecycleEvents_CampaignId_ClubId",
            table: "CampaignLifecycleEvents",
            columns: new[] { "CampaignId", "ClubId" });

        migrationBuilder.CreateIndex(
            name: "IX_CampaignLifecycleEvents_ClubId",
            table: "CampaignLifecycleEvents",
            column: "ClubId");
    }
}
