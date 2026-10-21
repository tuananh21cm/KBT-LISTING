using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Persist saved Product opportunity keywords independently of the per-shop snapshot.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814160000_AddProductOpportunitySavedKeywords")]
public class AddProductOpportunitySavedKeywords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductOpportunitySavedKeywords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                Keyword = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                KeywordNormalized = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                LeadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                PicUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SavedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductOpportunitySavedKeywords", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductOpportunitySavedKeywords_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProductOpportunitySavedKeywords_AspNetUsers_SavedByUserId",
                    column: x => x.SavedByUserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_ProductOpportunitySavedKeywords_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_OwnerId_KeywordNormalized",
            table: "ProductOpportunitySavedKeywords",
            columns: new[] { "OwnerId", "KeywordNormalized" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_ShopId",
            table: "ProductOpportunitySavedKeywords",
            column: "ShopId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunitySavedKeywords_SavedByUserId",
            table: "ProductOpportunitySavedKeywords",
            column: "SavedByUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductOpportunitySavedKeywords");
    }
}
