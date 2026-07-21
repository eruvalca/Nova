using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nova.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivalLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ArchivedById",
                table: "Teams",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleStatus",
                table: "Teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "PlayerTags",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ArchivedById",
                table: "PlayerTags",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleStatus",
                table: "PlayerTags",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                table: "Players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ArchivedById",
                table: "Players",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleStatus",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Teams_LifecycleArchiveMetadata",
                table: "Teams",
                sql: "(\"LifecycleStatus\" = 0 AND \"ArchivedAt\" IS NULL AND \"ArchivedById\" IS NULL) OR (\"LifecycleStatus\" = 1 AND \"ArchivedAt\" IS NOT NULL AND \"ArchivedById\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PlayerTags_LifecycleArchiveMetadata",
                table: "PlayerTags",
                sql: "(\"LifecycleStatus\" = 0 AND \"ArchivedAt\" IS NULL AND \"ArchivedById\" IS NULL) OR (\"LifecycleStatus\" = 1 AND \"ArchivedAt\" IS NOT NULL AND \"ArchivedById\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Players_LifecycleArchiveMetadata",
                table: "Players",
                sql: "(\"LifecycleStatus\" = 0 AND \"ArchivedAt\" IS NULL AND \"ArchivedById\" IS NULL) OR (\"LifecycleStatus\" = 1 AND \"ArchivedAt\" IS NOT NULL AND \"ArchivedById\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Teams_LifecycleArchiveMetadata",
                table: "Teams");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PlayerTags_LifecycleArchiveMetadata",
                table: "PlayerTags");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Players_LifecycleArchiveMetadata",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ArchivedById",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "PlayerTags");

            migrationBuilder.DropColumn(
                name: "ArchivedById",
                table: "PlayerTags");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "PlayerTags");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "ArchivedById",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "Players");
        }
    }
}
