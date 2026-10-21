using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// TikTok Health Center score snapshot columns on Shops (AHR + Shop Performance Score).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260824160000_AddShopTikTokScores")]
public class AddShopTikTokScores : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AhrScore",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ShopPerformanceScore",
            table: "Shops",
            type: "numeric(3,1)",
            precision: 3,
            scale: 1,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ProductSatisfactionScore",
            table: "Shops",
            type: "numeric(3,1)",
            precision: 3,
            scale: 1,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FulfillmentScore",
            table: "Shops",
            type: "numeric(3,1)",
            precision: 3,
            scale: 1,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CustomerServiceScore",
            table: "Shops",
            type: "numeric(3,1)",
            precision: 3,
            scale: 1,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TikTokShopScoreSyncedAt",
            table: "Shops",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AhrScore", table: "Shops");
        migrationBuilder.DropColumn(name: "ShopPerformanceScore", table: "Shops");
        migrationBuilder.DropColumn(name: "ProductSatisfactionScore", table: "Shops");
        migrationBuilder.DropColumn(name: "FulfillmentScore", table: "Shops");
        migrationBuilder.DropColumn(name: "CustomerServiceScore", table: "Shops");
        migrationBuilder.DropColumn(name: "TikTokShopScoreSyncedAt", table: "Shops");
    }
}
