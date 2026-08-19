using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nova.Data.Migrations;

/// <inheritdoc />
public partial class AddTagDefinitionMutationReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TagDefinitionMutationReceipts",
            columns: table => new
            {
                TagDefinitionMutationReceiptId = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                PlayerTagId = table.Column<long>(type: "bigint", nullable: false),
                MutationType = table.Column<int>(type: "integer", nullable: false),
                ClubId = table.Column<long>(type: "bigint", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedById = table.Column<long>(type: "bigint", nullable: false),
                ModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ModifiedById = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TagDefinitionMutationReceipts", x => x.TagDefinitionMutationReceiptId);
                table.ForeignKey(
                    name: "FK_TagDefinitionMutationReceipts_Clubs_ClubId",
                    column: x => x.ClubId,
                    principalTable: "Clubs",
                    principalColumn: "ClubId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TagDefinitionMutationReceipts_ClubId",
            table: "TagDefinitionMutationReceipts",
            column: "ClubId");

        migrationBuilder.CreateIndex(
            name: "IX_TagDefinitionMutationReceipts_OperationId",
            table: "TagDefinitionMutationReceipts",
            column: "OperationId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "TagDefinitionMutationReceipts");
    }
}
