using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTagDefinitionNormalizedNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId",
                table: "PlayerTags");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "PlayerTags",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE \"PlayerTags\" SET \"NormalizedName\" = UPPER(TRIM(\"Name\"));");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId_NormalizedName",
                table: "PlayerTags",
                columns: new[] { "ClubId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerTags_ClubId_NormalizedName",
                table: "PlayerTags");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "PlayerTags");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerTags_ClubId",
                table: "PlayerTags",
                column: "ClubId");
        }
    }
}
