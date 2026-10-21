using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Snapshot table for TikTok Shop Product opportunities (trending keywords) plus shop last-sync stamp.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260814120000_AddProductOpportunities")]
public class AddProductOpportunities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ProductOpportunitiesSyncedAt",
            table: "Shops",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ProductOpportunities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                LeadId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LeadName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                PicUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SearchVolume = table.Column<int>(type: "integer", nullable: false),
                OnlineProducts = table.Column<int>(type: "integer", nullable: true),
                OnlineProductsRaw = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                SearchVolumeRingRatio = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                OnlineProductsRingRatio = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Level1CateId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Level1CateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Level2CateId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Level2CateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Level3CateId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                Level3CateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                TagCodes = table.Column<string>(type: "jsonb", nullable: true),
                LeadSourceTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LeadClusterSourceTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                OpportunityType = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductOpportunities", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductOpportunities_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunities_ShopId_LeadId",
            table: "ProductOpportunities",
            columns: new[] { "ShopId", "LeadId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductOpportunities_ShopId_SearchVolume",
            table: "ProductOpportunities",
            columns: new[] { "ShopId", "SearchVolume" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductOpportunities");
        migrationBuilder.DropColumn(name: "ProductOpportunitiesSyncedAt", table: "Shops");
    }
}
