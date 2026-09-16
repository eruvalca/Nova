using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
internal partial class AddPlayerCreationReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlayerCreationReceipts",
            columns: table => new
            {
                PlayerCreationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ResultJson = table.Column<string>(type: "text", nullable: false),
                RecoveryExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlayerCreationReceipts", x => x.PlayerCreationReceiptId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlayerCreationReceipts_ClubId_OperationId",
            table: "PlayerCreationReceipts",
            columns: ["ClubId", "OperationId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlayerCreationReceipts_RecoveryExpiresAt_PlayerCreationRece~",
            table: "PlayerCreationReceipts",
            columns: ["RecoveryExpiresAt", "PlayerCreationReceiptId"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PlayerCreationReceipts");
    }
}
