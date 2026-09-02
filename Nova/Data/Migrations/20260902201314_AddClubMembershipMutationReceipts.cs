using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddClubMembershipMutationReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ClubMembershipMutationReceipts",
            columns: table => new
            {
                ClubMembershipMutationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                MemberUserId = table.Column<long>(type: "bigint", nullable: false),
                MutationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ClubMembershipMutationReceipts", x => x.ClubMembershipMutationReceiptId);
                table.ForeignKey(
                    name: "FK_ClubMembershipMutationReceipts_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ClubMembershipMutationReceipts_ClubId",
            table: "ClubMembershipMutationReceipts",
            column: "ClubId");

        migrationBuilder.CreateIndex(
            name: "IX_ClubMembershipMutationReceipts_OperationId",
            table: "ClubMembershipMutationReceipts",
            column: "OperationId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ClubMembershipMutationReceipts");
    }
}
