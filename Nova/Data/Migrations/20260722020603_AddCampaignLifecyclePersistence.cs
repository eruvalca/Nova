using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignLifecyclePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "Campaigns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClosedById",
                table: "Campaigns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Campaigns_CampaignId_ClubId",
                table: "Campaigns",
                columns: new[] { "CampaignId", "ClubId" });

            migrationBuilder.CreateTable(
                name: "CampaignLifecycleEvents",
                columns: table => new
                {
                    CampaignLifecycleEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    ClubId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<long>(type: "bigint", nullable: false),
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_StatusClosureMetadata",
                table: "Campaigns",
                sql: "(\"Status\" = 0 AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"Status\" = 1 AND \"ClosedAt\" IS NOT NULL AND \"ClosedById\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignLifecycleEvents_CampaignId_ClubId",
                table: "CampaignLifecycleEvents",
                columns: new[] { "CampaignId", "ClubId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignLifecycleEvents_ClubId",
                table: "CampaignLifecycleEvents",
                column: "ClubId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignLifecycleEvents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Campaigns_CampaignId_ClubId",
                table: "Campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_StatusClosureMetadata",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ClosedById",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Campaigns");
        }
    }
}
