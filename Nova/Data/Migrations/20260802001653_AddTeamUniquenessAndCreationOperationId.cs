using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamUniquenessAndCreationOperationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreationOperationId",
                table: "Teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ClubId_CreationOperationId",
                table: "Teams",
                columns: new[] { "ClubId", "CreationOperationId" },
                unique: true,
                filter: "\"CreationOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ClubId_Name_GraduationYear",
                table: "Teams",
                columns: new[] { "ClubId", "Name", "GraduationYear" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Teams_ClubId_CreationOperationId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Teams_ClubId_Name_GraduationYear",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "CreationOperationId",
                table: "Teams");
        }
    }
}
