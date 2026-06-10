using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueNovaUserPhotoPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NovaUserPhotos_NovaUserId",
                table: "NovaUserPhotos");

            migrationBuilder.CreateIndex(
                name: "IX_NovaUserPhotos_NovaUserId",
                table: "NovaUserPhotos",
                column: "NovaUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NovaUserPhotos_NovaUserId",
                table: "NovaUserPhotos");

            migrationBuilder.CreateIndex(
                name: "IX_NovaUserPhotos_NovaUserId",
                table: "NovaUserPhotos",
                column: "NovaUserId");
        }
    }
}
