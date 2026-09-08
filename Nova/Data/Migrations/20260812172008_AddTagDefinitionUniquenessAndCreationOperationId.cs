using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class AddTagDefinitionUniquenessAndCreationOperationId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId",
            table: "PlayerTags");

        migrationBuilder.AddColumn<Guid>(
            name: "CreationOperationId",
            table: "PlayerTags",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedName",
            table: "PlayerTags",
            type: "text",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"PlayerTags\" SET \"NormalizedName\" = upper(trim(\"Name\"));");

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_CreationOperationId",
            table: "PlayerTags",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "ClubId", "CreationOperationId" },
#pragma warning restore CA1861
            unique: true,
            filter: "\"CreationOperationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_LifecycleStatus",
            table: "PlayerTags",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "ClubId", "LifecycleStatus" });
#pragma warning restore CA1861

        migrationBuilder.CreateIndex(
            name: "IX_PlayerTags_ClubId_NormalizedName",
            table: "PlayerTags",
#pragma warning disable CA1861 // Migration column arrays describe one-time schema operations and are not hot-path allocations.
            columns: new[] { "ClubId", "NormalizedName" },
#pragma warning restore CA1861
            unique: true,
            filter: "\"NormalizedName\" IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_CreationOperationId",
            table: "PlayerTags");

        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_LifecycleStatus",
            table: "PlayerTags");

        migrationBuilder.DropIndex(
            name: "IX_PlayerTags_ClubId_NormalizedName",
            table: "PlayerTags");

        migrationBuilder.DropColumn(
            name: "CreationOperationId",
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
