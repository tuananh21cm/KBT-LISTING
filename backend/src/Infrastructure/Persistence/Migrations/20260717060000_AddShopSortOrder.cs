using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260717060000_AddShopSortOrder")]
public class AddShopSortOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "SortOrder",
            table: "Shops",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Preserve current list UX (newest first) before admin drag-and-drop.
        migrationBuilder.Sql(
            """
            WITH ordered AS (
                SELECT "Id", (ROW_NUMBER() OVER (ORDER BY "CreatedAt" DESC) - 1) AS "NewSortOrder"
                FROM "Shops"
            )
            UPDATE "Shops" AS s
            SET "SortOrder" = ordered."NewSortOrder"
            FROM ordered
            WHERE s."Id" = ordered."Id";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Shops_SortOrder",
            table: "Shops",
            column: "SortOrder");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Shops_SortOrder",
            table: "Shops");

        migrationBuilder.DropColumn(
            name: "SortOrder",
            table: "Shops");
    }
}
