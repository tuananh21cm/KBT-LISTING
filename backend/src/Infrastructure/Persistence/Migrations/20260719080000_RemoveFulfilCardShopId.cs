using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719080000_RemoveFulfilCardShopId")]
public class RemoveFulfilCardShopId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent: original AddFulfilManagement may have created ShopId before it was removed from the model.
        migrationBuilder.Sql(
            """
            ALTER TABLE "FulfilCards" DROP CONSTRAINT IF EXISTS "FK_FulfilCards_Shops_ShopId";
            DROP INDEX IF EXISTS "IX_FulfilCards_ShopId";
            ALTER TABLE "FulfilCards" DROP COLUMN IF EXISTS "ShopId";
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ShopId",
            table: "FulfilCards",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_FulfilCards_ShopId",
            table: "FulfilCards",
            column: "ShopId");

        migrationBuilder.AddForeignKey(
            name: "FK_FulfilCards_Shops_ShopId",
            table: "FulfilCards",
            column: "ShopId",
            principalTable: "Shops",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }
}
