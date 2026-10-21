using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719090000_FulfilCardManyToManyAndIsActive")]
public class FulfilCardManyToManyAndIsActive : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FulfilAccountCards",
            columns: table => new
            {
                FulfilAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                FulfilCardId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilAccountCards", x => new { x.FulfilAccountId, x.FulfilCardId });
                table.ForeignKey(
                    name: "FK_FulfilAccountCards_FulfilAccounts_FulfilAccountId",
                    column: x => x.FulfilAccountId,
                    principalTable: "FulfilAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_FulfilAccountCards_FulfilCards_FulfilCardId",
                    column: x => x.FulfilCardId,
                    principalTable: "FulfilCards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FulfilAccountCards_FulfilCardId",
            table: "FulfilAccountCards",
            column: "FulfilCardId");

        // Migrate existing 1:N ownership into join table when columns still exist.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'FulfilCards' AND column_name = 'FulfilAccountId'
              ) THEN
                INSERT INTO "FulfilAccountCards" ("FulfilAccountId", "FulfilCardId")
                SELECT "FulfilAccountId", "Id"
                FROM "FulfilCards"
                WHERE "FulfilAccountId" IS NOT NULL
                ON CONFLICT DO NOTHING;
              END IF;
            END $$;
            """);

        migrationBuilder.Sql(
            """
            ALTER TABLE "FulfilCards" DROP CONSTRAINT IF EXISTS "FK_FulfilCards_FulfilAccounts_FulfilAccountId";
            DROP INDEX IF EXISTS "IX_FulfilCards_FulfilAccountId";
            DROP INDEX IF EXISTS "IX_FulfilCards_FulfilAccountId_IsActiveOnAccount";
            ALTER TABLE "FulfilCards" DROP COLUMN IF EXISTS "FulfilAccountId";
            """);

        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'FulfilCards' AND column_name = 'IsActiveOnAccount'
              ) THEN
                ALTER TABLE "FulfilCards" RENAME COLUMN "IsActiveOnAccount" TO "IsActive";
              ELSIF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'FulfilCards' AND column_name = 'IsActive'
              ) THEN
                ALTER TABLE "FulfilCards" ADD COLUMN "IsActive" boolean NOT NULL DEFAULT true;
              END IF;
            END $$;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_IsActive",
            table: "FulfilCards",
            column: "IsActive");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FulfilAccountCards");

        migrationBuilder.DropIndex(
            name: "IX_FulfilCards_IsActive",
            table: "FulfilCards");

        migrationBuilder.RenameColumn(
            name: "IsActive",
            table: "FulfilCards",
            newName: "IsActiveOnAccount");

        migrationBuilder.AddColumn<Guid>(
            name: "FulfilAccountId",
            table: "FulfilCards",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
    }
}
