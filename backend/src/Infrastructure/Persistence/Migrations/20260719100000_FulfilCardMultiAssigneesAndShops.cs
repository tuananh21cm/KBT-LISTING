using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719100000_FulfilCardMultiAssigneesAndShops")]
public class FulfilCardMultiAssigneesAndShops : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FulfilCardAssignees",
            columns: table => new
            {
                FulfilCardId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilCardAssignees", x => new { x.FulfilCardId, x.UserId });
                table.ForeignKey(
                    name: "FK_FulfilCardAssignees_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_FulfilCardAssignees_FulfilCards_FulfilCardId",
                    column: x => x.FulfilCardId,
                    principalTable: "FulfilCards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "FulfilCardShops",
            columns: table => new
            {
                FulfilCardId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FulfilCardShops", x => new { x.FulfilCardId, x.ShopId });
                table.ForeignKey(
                    name: "FK_FulfilCardShops_FulfilCards_FulfilCardId",
                    column: x => x.FulfilCardId,
                    principalTable: "FulfilCards",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_FulfilCardShops_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCardAssignees_UserId",
            table: "FulfilCardAssignees",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCardShops_ShopId",
            table: "FulfilCardShops",
            column: "ShopId");

        // Migrate legacy single assignee into join table.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'FulfilCards' AND column_name = 'AssignedToUserId'
              ) THEN
                INSERT INTO "FulfilCardAssignees" ("FulfilCardId", "UserId")
                SELECT "Id", "AssignedToUserId"
                FROM "FulfilCards"
                WHERE "AssignedToUserId" IS NOT NULL
                ON CONFLICT DO NOTHING;

                ALTER TABLE "FulfilCards" DROP CONSTRAINT IF EXISTS "FK_FulfilCards_AspNetUsers_AssignedToUserId";
                DROP INDEX IF EXISTS "IX_FulfilCards_AssignedToUserId";
                ALTER TABLE "FulfilCards" DROP COLUMN IF EXISTS "AssignedToUserId";
              END IF;
            END $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FulfilCardAssignees");
        migrationBuilder.DropTable(name: "FulfilCardShops");

        migrationBuilder.AddColumn<Guid>(
            name: "AssignedToUserId",
            table: "FulfilCards",
            type: "uuid",
            nullable: true);
    }
}
