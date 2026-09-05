using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddPlayerImportReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlayerImportReceipts",
            columns: table => new
            {
                PlayerImportReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                FileSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FileLength = table.Column<int>(type: "integer", nullable: false),
                ConfirmationTokenSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ResultJson = table.Column<string>(type: "text", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecoveryExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlayerImportReceipts", x => x.PlayerImportReceiptId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlayerImportReceipts_ClubId_OperationId",
            table: "PlayerImportReceipts",
            columns: new[] { "ClubId", "OperationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlayerImportReceipts_CreatedAt",
            table: "PlayerImportReceipts",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_PlayerImportReceipts_RecoveryExpiresAt",
            table: "PlayerImportReceipts",
            column: "RecoveryExpiresAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PlayerImportReceipts");
    }
}
