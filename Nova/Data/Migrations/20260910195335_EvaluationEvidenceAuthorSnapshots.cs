using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class EvaluationEvidenceAuthorSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AuthorDisplayName",
            table: "Notes",
            type: "text",
            nullable: false);

        migrationBuilder.AddColumn<string>(
            name: "AuthorDisplayName",
            table: "CampaignTagApplications",
            type: "text",
            nullable: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AuthorDisplayName", table: "Notes");
        migrationBuilder.DropColumn(name: "AuthorDisplayName", table: "CampaignTagApplications");
    }
}
