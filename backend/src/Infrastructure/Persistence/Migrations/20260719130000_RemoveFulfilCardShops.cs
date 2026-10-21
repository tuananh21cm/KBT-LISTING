using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719130000_RemoveFulfilCardShops")]
public class RemoveFulfilCardShops : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP TABLE IF EXISTS "FulfilCardShops";""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
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
            name: "IX_FulfilCardShops_ShopId",
            table: "FulfilCardShops",
            column: "ShopId");
    }
}
