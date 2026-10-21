using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Saved keywords are unique per shop (not per seller). Same TikTok lead_id across shops
/// no longer shares one bookmark.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260818120000_SavedKeywordsPerShop")]
public class SavedKeywordsPerShop : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM "ProductOpportunitySavedKeywords"
            WHERE "ShopId" IS NULL;
            """);

        migrationBuilder.Sql(
            """
            DELETE FROM "ProductOpportunitySavedKeywords" a
            USING "ProductOpportunitySavedKeywords" b
            WHERE a."ShopId" = b."ShopId"
              AND a."KeywordNormalized" = b."KeywordNormalized"
              AND a."Id" < b."Id";
            """);

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunitySavedKeywords_OwnerId_KeywordNormalized",
            table: "ProductOpportunitySavedKeywords");

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunitySavedKeywords_ShopId",
            table: "ProductOpportunitySavedKeywords");

        migrationBuilder.DropForeignKey(
            name: "FK_ProductOpportunitySavedKeywords_Shops_ShopId",
            table: "ProductOpportunitySavedKeywords");

        migrationBuilder.AlterColumn<Guid>(
            name: "ShopId",
            table: "ProductOpportunitySavedKeywords",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AddForeignKey(
            name: "FK_ProductOpportunitySavedKeywords_Shops_ShopId",
            table: "ProductOpportunitySavedKeywords",
            column: "ShopId",
            principalTable: "Shops",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_ShopId_KeywordNormalized",
            table: "ProductOpportunitySavedKeywords",
            columns: new[] { "ShopId", "KeywordNormalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_OwnerId",
            table: "ProductOpportunitySavedKeywords",
            column: "OwnerId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunitySavedKeywords_ShopId_KeywordNormalized",
            table: "ProductOpportunitySavedKeywords");

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunitySavedKeywords_OwnerId",
            table: "ProductOpportunitySavedKeywords");

        migrationBuilder.DropForeignKey(
            name: "FK_ProductOpportunitySavedKeywords_Shops_ShopId",
            table: "ProductOpportunitySavedKeywords");

        migrationBuilder.AlterColumn<Guid>(
            name: "ShopId",
            table: "ProductOpportunitySavedKeywords",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AddForeignKey(
            name: "FK_ProductOpportunitySavedKeywords_Shops_ShopId",
            table: "ProductOpportunitySavedKeywords",
            column: "ShopId",
            principalTable: "Shops",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_OwnerId_KeywordNormalized",
            table: "ProductOpportunitySavedKeywords",
            columns: new[] { "OwnerId", "KeywordNormalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_ShopId",
            table: "ProductOpportunitySavedKeywords",
            column: "ShopId");
    }
}
