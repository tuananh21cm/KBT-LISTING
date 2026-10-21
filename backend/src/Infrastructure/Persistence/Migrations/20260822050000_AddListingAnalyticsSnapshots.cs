using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Daily TikTok Compass product metrics per shop product (one row per Vietnam day).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260822050000_AddListingAnalyticsSnapshots")]
public class AddListingAnalyticsSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ListingAnalyticsSnapshots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                TikTokProductId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: true),
                SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                RangeStart = table.Column<DateOnly>(type: "date", nullable: false),
                RangeEnd = table.Column<DateOnly>(type: "date", nullable: false),
                Preset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ProductName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                MainImage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                Gmv = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Orders = table.Column<int>(type: "integer", nullable: false),
                Impressions = table.Column<long>(type: "bigint", nullable: false),
                Clicks = table.Column<long>(type: "bigint", nullable: false),
                Ctr = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                AddToCartUnits = table.Column<int>(type: "integer", nullable: true),
                AddToCartBuyers = table.Column<int>(type: "integer", nullable: true),
                GmvRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                OrdersRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                ImpressionsRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                ClicksRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                CtrRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                AddToCartUnitsRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                AddToCartBuyersRatio = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                RawStatsJson = table.Column<string>(type: "jsonb", nullable: true),
                SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingAnalyticsSnapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_ListingAnalyticsSnapshots_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ListingAnalyticsSnapshots_Listings_ListingId",
                    column: x => x.ListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ListingAnalyticsSnapshots_ShopId_TikTokProductId_SnapshotDate",
            table: "ListingAnalyticsSnapshots",
            columns: ["ShopId", "TikTokProductId", "SnapshotDate"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ListingAnalyticsSnapshots_OwnerId_SnapshotDate",
            table: "ListingAnalyticsSnapshots",
            columns: ["OwnerId", "SnapshotDate"]);

        migrationBuilder.CreateIndex(
            name: "IX_ListingAnalyticsSnapshots_ListingId_SnapshotDate",
            table: "ListingAnalyticsSnapshots",
            columns: ["ListingId", "SnapshotDate"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ListingAnalyticsSnapshots");
    }
}
