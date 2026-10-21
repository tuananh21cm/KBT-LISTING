using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260715210000_AddListingsAndListingVariations")]
public class AddListingsAndListingVariations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Listings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FourSellerListingId = table.Column<long>(type: "bigint", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                ShopId = table.Column<Guid>(type: "uuid", nullable: true),
                FourSellerShopId = table.Column<long>(type: "bigint", nullable: true),
                Platform = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SiteStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ProductName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                MainImage = table.Column<string>(type: "text", nullable: true),
                Site = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                LowPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                HighPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                AvailableStock = table.Column<int>(type: "integer", nullable: true),
                VariationCount = table.Column<int>(type: "integer", nullable: false),
                CategoryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CategoryName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                WarehouseId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SourceUrl = table.Column<string>(type: "text", nullable: true),
                DescriptionHtml = table.Column<string>(type: "text", nullable: true),
                PackageWeight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                PackageLength = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                PackageWidth = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                PackageHeight = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                WeightUnit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                DimensionUnit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                ErpStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PublishStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                TikTokProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PlatformCreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PlatformUpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastPushAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SyncError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Listings", x => x.Id);
                table.ForeignKey(
                    name: "FK_Listings_AspNetUsers_OwnerId",
                    column: x => x.OwnerId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Listings_Shops_ShopId",
                    column: x => x.ShopId,
                    principalTable: "Shops",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "ListingVariations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                FourSellerSkuId = table.Column<long>(type: "bigint", nullable: true),
                SellerSku = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                MainImage = table.Column<string>(type: "text", nullable: true),
                SalePrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                GlobalPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                Quantity = table.Column<int>(type: "integer", nullable: true),
                VariantAttributes = table.Column<string>(type: "jsonb", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ListingVariations", x => x.Id);
                table.ForeignKey(
                    name: "FK_ListingVariations_Listings_ListingId",
                    column: x => x.ListingId,
                    principalTable: "Listings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Listings_FourSellerListingId",
            table: "Listings",
            column: "FourSellerListingId",
            unique: true);

        migrationBuilder.CreateIndex(name: "IX_Listings_OwnerId", table: "Listings", column: "OwnerId");
        migrationBuilder.CreateIndex(name: "IX_Listings_PageType", table: "Listings", column: "PageType");
        migrationBuilder.CreateIndex(name: "IX_Listings_SiteStatus", table: "Listings", column: "SiteStatus");
        migrationBuilder.CreateIndex(name: "IX_Listings_ShopId", table: "Listings", column: "ShopId");
        migrationBuilder.CreateIndex(name: "IX_Listings_Platform", table: "Listings", column: "Platform");
        migrationBuilder.CreateIndex(name: "IX_Listings_LastSyncedAt", table: "Listings", column: "LastSyncedAt");
        migrationBuilder.CreateIndex(
            name: "IX_Listings_PageType_SiteStatus",
            table: "Listings",
            columns: new[] { "PageType", "SiteStatus" });

        migrationBuilder.CreateIndex(
            name: "IX_ListingVariations_ListingId",
            table: "ListingVariations",
            column: "ListingId");

        migrationBuilder.CreateIndex(
            name: "IX_ListingVariations_ListingId_FourSellerSkuId",
            table: "ListingVariations",
            columns: new[] { "ListingId", "FourSellerSkuId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ListingVariations");
        migrationBuilder.DropTable(name: "Listings");
    }
}
