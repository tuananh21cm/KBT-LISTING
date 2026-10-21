using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Canonical keywords unique by TikTok lead_id; shops attach via ProductOpportunityShopLinks.
/// Collapses per-shop snapshot copies that shared the same lead_id.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260818100000_AddProductOpportunityShopLinks")]
public class AddProductOpportunityShopLinks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductOpportunityShopLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductOpportunityShopLinks", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductOpportunityShopLinks_ProductOpportunities_ProductOpportunityId",
                    column: x => x.ProductOpportunityId,
                    principalTable: "ProductOpportunities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ProductOpportunityShopLinks_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO "ProductOpportunityShopLinks" ("Id", "ShopId", "ProductOpportunityId", "CreatedAt")
            SELECT gen_random_uuid(), po."ShopId", keeper."Id", now()
            FROM "ProductOpportunities" po
            INNER JOIN (
                SELECT DISTINCT ON ("LeadId") "Id", "LeadId"
                FROM "ProductOpportunities"
                WHERE "LeadId" <> ''
                ORDER BY "LeadId", COALESCE("UpdatedAt", "CreatedAt") DESC, "CreatedAt" DESC, "Id" DESC
            ) keeper ON keeper."LeadId" = po."LeadId";
            """);

        migrationBuilder.Sql(
            """
            DELETE FROM "ProductOpportunities" po
            WHERE po."Id" NOT IN (
                SELECT DISTINCT ON ("LeadId") "Id"
                FROM "ProductOpportunities"
                WHERE "LeadId" <> ''
                ORDER BY "LeadId", COALESCE("UpdatedAt", "CreatedAt") DESC, "CreatedAt" DESC, "Id" DESC
            );
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_ProductOpportunities_Shops_ShopId",
            table: "ProductOpportunities");

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunities_ShopId_LeadId",
            table: "ProductOpportunities");

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunities_ShopId_SearchVolume",
            table: "ProductOpportunities");

        migrationBuilder.DropColumn(
            name: "ShopId",
            table: "ProductOpportunities");

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunities_LeadId",
            table: "ProductOpportunities",
            column: "LeadId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunities_SearchVolume",
            table: "ProductOpportunities",
            column: "SearchVolume");

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunityShopLinks_ProductOpportunityId",
            table: "ProductOpportunityShopLinks",
            column: "ProductOpportunityId");

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunityShopLinks_ShopId_ProductOpportunityId",
            table: "ProductOpportunityShopLinks",
            columns: new[] { "ShopId", "ProductOpportunityId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductOpportunityShopLinks");

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunities_LeadId",
            table: "ProductOpportunities");

        migrationBuilder.DropIndex(
            name: "IX_ProductOpportunities_SearchVolume",
            table: "ProductOpportunities");

        migrationBuilder.AddColumn<Guid>(
            name: "ShopId",
            table: "ProductOpportunities",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunities_ShopId_LeadId",
            table: "ProductOpportunities",
            columns: new[] { "ShopId", "LeadId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunities_ShopId_SearchVolume",
            table: "ProductOpportunities",
            columns: new[] { "ShopId", "SearchVolume" });

        migrationBuilder.AddForeignKey(
            name: "FK_ProductOpportunities_Shops_ShopId",
            table: "ProductOpportunities",
            column: "ShopId",
            principalTable: "Shops",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
