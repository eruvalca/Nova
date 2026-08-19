using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddEvaluationNoteMutationReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EvaluationNoteMutationReceipts",
            columns: table => new
            {
                EvaluationNoteMutationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                NoteId = table.Column<long>(type: "bigint", nullable: false),
                MutationType = table.Column<int>(type: "integer", nullable: false),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationNoteMutationReceipts", x => x.EvaluationNoteMutationReceiptId);
                table.ForeignKey(
                    name: "FK_EvaluationNoteMutationReceipts_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EvaluationNoteMutationReceipts_ClubId",
            table: "EvaluationNoteMutationReceipts",
            column: "ClubId");

        migrationBuilder.CreateIndex(
            name: "IX_EvaluationNoteMutationReceipts_OperationId",
            table: "EvaluationNoteMutationReceipts",
            column: "OperationId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EvaluationNoteMutationReceipts");
    }
}
