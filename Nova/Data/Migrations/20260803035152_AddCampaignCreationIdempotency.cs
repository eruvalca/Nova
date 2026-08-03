using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignCreationIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Seasons_SeasonId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_ClubId",
                table: "Campaigns");

            migrationBuilder.AddColumn<Guid>(
                name: "CreationOperationId",
                table: "Seasons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreationOperationId",
                table: "Campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SeasonCreatedInline",
                table: "Campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Seasons_SeasonId_ClubId",
                table: "Seasons",
                columns: new[] { "SeasonId", "ClubId" });

            migrationBuilder.CreateIndex(
                name: "IX_Seasons_ClubId_CreationOperationId",
                table: "Seasons",
                columns: new[] { "ClubId", "CreationOperationId" },
                unique: true,
                filter: "\"CreationOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_ClubId_CreationOperationId",
                table: "Campaigns",
                columns: new[] { "ClubId", "CreationOperationId" },
                unique: true,
                filter: "\"CreationOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_ClubId_SeasonId_Name",
                table: "Campaigns",
                columns: new[] { "ClubId", "SeasonId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_SeasonId_ClubId",
                table: "Campaigns",
                columns: new[] { "SeasonId", "ClubId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Seasons_SeasonId_ClubId",
                table: "Campaigns",
                columns: new[] { "SeasonId", "ClubId" },
                principalTable: "Seasons",
                principalColumns: new[] { "SeasonId", "ClubId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Seasons_SeasonId_ClubId",
                table: "Campaigns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Seasons_SeasonId_ClubId",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Seasons_ClubId_CreationOperationId",
                table: "Seasons");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_ClubId_CreationOperationId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_ClubId_SeasonId_Name",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_SeasonId_ClubId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "CreationOperationId",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "CreationOperationId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SeasonCreatedInline",
                table: "Campaigns");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_ClubId",
                table: "Campaigns",
                column: "ClubId");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Seasons_SeasonId",
                table: "Campaigns",
                column: "SeasonId",
                principalTable: "Seasons",
                principalColumn: "SeasonId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
