using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Persist «Shop này bán gì» AI result per shop so it can be reopened without calling OpenAI.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814190000_AddShopKeywordResearches")]
public class AddShopKeywordResearches : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ShopKeywordResearches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                SnapshotSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AnalyzedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShopKeywordResearches", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShopKeywordResearches_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ShopKeywordResearches_ShopId",
            table: "ShopKeywordResearches",
            column: "ShopId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ShopKeywordResearches");
    }
}
