using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719110000_FulfilAccountMultiAssignees")]
public class FulfilAccountMultiAssignees : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FulfilAccountAssignees",
            columns: table => new
            {
                FulfilAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilAccountAssignees", x => new { x.FulfilAccountId, x.UserId });
                table.ForeignKey(
                    name: "FK_FulfilAccountAssignees_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_FulfilAccountAssignees_FulfilAccounts_FulfilAccountId",
                    column: x => x.FulfilAccountId,
                    principalTable: "FulfilAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccountAssignees_UserId",
            table: "FulfilAccountAssignees",
            column: "UserId");

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'FulfilAccounts' AND column_name = 'OperatorUserId'
              ) THEN
                INSERT INTO "FulfilAccountAssignees" ("FulfilAccountId", "UserId")
                SELECT "Id", "OperatorUserId"
                FROM "FulfilAccounts"
                WHERE "OperatorUserId" IS NOT NULL
                ON CONFLICT DO NOTHING;

                ALTER TABLE "FulfilAccounts" DROP CONSTRAINT IF EXISTS "FK_FulfilAccounts_AspNetUsers_OperatorUserId";
                DROP INDEX IF EXISTS "IX_FulfilAccounts_OperatorUserId";
                ALTER TABLE "FulfilAccounts" DROP COLUMN IF EXISTS "OperatorUserId";
              END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FulfilAccountAssignees");

        migrationBuilder.AddColumn<Guid>(
            name: "OperatorUserId",
            table: "FulfilAccounts",
            type: "uuid",
            nullable: true);
    }
}
