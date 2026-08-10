using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddCampaignTagApplicationOperationIds : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "CreationOperationId",
            table: "CampaignTagApplications",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CampaignTagApplicationRemovalReceipts",
            columns: table => new
            {
                CampaignTagApplicationRemovalReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RemovalOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                CampaignTagApplicationId = table.Column<long>(type: "bigint", nullable: false),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
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

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplications_ClubId_CreationOperationId",
            table: "CampaignTagApplications",
            columns: new[] { "ClubId", "CreationOperationId" },
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplicationRemovalReceipts_ClubId",
            table: "CampaignTagApplicationRemovalReceipts",
            column: "ClubId");

        migrationBuilder.CreateIndex(
            name: "IX_CampaignTagApplicationRemovalReceipts_RemovalOperationId",
            table: "CampaignTagApplicationRemovalReceipts",
            column: "RemovalOperationId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CampaignTagApplicationRemovalReceipts");

        migrationBuilder.DropIndex(
            name: "IX_CampaignTagApplications_ClubId_CreationOperationId",
            table: "CampaignTagApplications");

        migrationBuilder.DropColumn(
            name: "CreationOperationId",
            table: "CampaignTagApplications");
    }
}
