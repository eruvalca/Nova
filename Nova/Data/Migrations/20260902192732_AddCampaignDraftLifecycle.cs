using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddCampaignDraftLifecycle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Campaigns_StatusClosureMetadata",
            table: "Campaigns");

        migrationBuilder.DropColumn(
            name: "InitialEnrolledPlayerCount",
            table: "Campaigns");

        migrationBuilder.CreateIndex(
            name: "UX_Campaigns_ClubId_Active",
            table: "Campaigns",
            column: "ClubId",
            unique: true,
            filter: "\"Status\" = 0");

        migrationBuilder.AddCheckConstraint(
            name: "CK_Campaigns_StatusClosureMetadata",
            table: "Campaigns",
            sql: "(\"Status\" IN (0, 2) AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"Status\" = 1 AND \"ClosedAt\" IS NOT NULL AND \"ClosedById\" IS NOT NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_Campaigns_ClubId_Active",
            table: "Campaigns");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Campaigns_StatusClosureMetadata",
            table: "Campaigns");

        migrationBuilder.AddColumn<int>(
            name: "InitialEnrolledPlayerCount",
            table: "Campaigns",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Campaigns_StatusClosureMetadata",
            table: "Campaigns",
            sql: "(\"Status\" = 0 AND \"ClosedAt\" IS NULL AND \"ClosedById\" IS NULL) OR (\"Status\" = 1 AND \"ClosedAt\" IS NOT NULL AND \"ClosedById\" IS NOT NULL)");
    }
}
