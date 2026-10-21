using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTikTokListingDetailTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandId",
                table: "ShopProducts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandName",
                table: "ShopProducts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryId",
                table: "ShopProducts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "ShopProducts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionHtml",
                table: "ShopProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DimensionUnit",
                table: "ShopProducts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FourSellerRawPayload",
                table: "ShopProducts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageHeight",
                table: "ShopProducts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageLength",
                table: "ShopProducts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageWeight",
                table: "ShopProducts",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackageWidth",
                table: "ShopProducts",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductName",
                table: "ShopProducts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductType",
                table: "ShopProducts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishStatus",
                table: "ShopProducts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefListingId",
                table: "ShopProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefShopId",
                table: "ShopProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SafetyWarning",
                table: "ShopProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteId",
                table: "ShopProducts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeChartId",
                table: "ShopProducts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncError",
                table: "ShopProducts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncStatus",
                table: "ShopProducts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseId",
                table: "ShopProducts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WeightUnit",
                table: "ShopProducts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ListingAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttrId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AttrName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AttrValue = table.Column<string>(type: "text", nullable: true),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingAttributes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingAttributes_ShopProducts_ShopProductId",
                        column: x => x.ShopProductId,
                        principalTable: "ShopProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListingCertifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    CertType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "text", nullable: true),
                    FileUrls = table.Column<string>(type: "jsonb", nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingCertifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingCertifications_ShopProducts_ShopProductId",
                        column: x => x.ShopProductId,
                        principalTable: "ShopProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListingSkus",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    FourSellerSkuId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TikTokSkuId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellerSku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SalePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    OriginalPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: true),
                    Barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VariantAttributes = table.Column<string>(type: "jsonb", nullable: true),
                    PackageWeight = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    PackageLength = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PackageWidth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PackageHeight = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingSkus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingSkus_ShopProducts_ShopProductId",
                        column: x => x.ShopProductId,
                        principalTable: "ShopProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ListingImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShopProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingSkuId = table.Column<Guid>(type: "uuid", nullable: true),
                    MediaType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TikTokImageId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListingImages_ListingSkus_ListingSkuId",
                        column: x => x.ListingSkuId,
                        principalTable: "ListingSkus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ListingImages_ShopProducts_ShopProductId",
                        column: x => x.ShopProductId,
                        principalTable: "ShopProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopProducts_ShopId_CategoryId",
                table: "ShopProducts",
                columns: new[] { "ShopId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShopProducts_ShopId_FourSellerListingId",
                table: "ShopProducts",
                columns: new[] { "ShopId", "FourSellerListingId" },
                unique: true,
                filter: "\"FourSellerListingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShopProducts_ShopId_ListingStatus",
                table: "ShopProducts",
                columns: new[] { "ShopId", "ListingStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingAttributes_ShopProductId_AttrId",
                table: "ListingAttributes",
                columns: new[] { "ShopProductId", "AttrId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListingCertifications_ShopProductId_CertType_ExternalId",
                table: "ListingCertifications",
                columns: new[] { "ShopProductId", "CertType", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingImages_ListingSkuId",
                table: "ListingImages",
                column: "ListingSkuId");

            migrationBuilder.CreateIndex(
                name: "IX_ListingImages_ShopProductId_MediaType_SortOrder",
                table: "ListingImages",
                columns: new[] { "ShopProductId", "MediaType", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ListingSkus_ShopProductId_SellerSku",
                table: "ListingSkus",
                columns: new[] { "ShopProductId", "SellerSku" },
                unique: true,
                filter: "\"SellerSku\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ListingSkus_ShopProductId_SortOrder",
                table: "ListingSkus",
                columns: new[] { "ShopProductId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListingAttributes");

            migrationBuilder.DropTable(
                name: "ListingCertifications");

            migrationBuilder.DropTable(
                name: "ListingImages");

            migrationBuilder.DropTable(
                name: "ListingSkus");

            migrationBuilder.DropIndex(
                name: "IX_ShopProducts_ShopId_CategoryId",
                table: "ShopProducts");

            migrationBuilder.DropIndex(
                name: "IX_ShopProducts_ShopId_FourSellerListingId",
                table: "ShopProducts");

            migrationBuilder.DropIndex(
                name: "IX_ShopProducts_ShopId_ListingStatus",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "BrandId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "BrandName",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "DescriptionHtml",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "DimensionUnit",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "FourSellerRawPayload",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "PackageHeight",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "PackageLength",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "PackageWeight",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "PackageWidth",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "ProductName",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "ProductType",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "PublishStatus",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "RefListingId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "RefShopId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "SafetyWarning",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "SizeChartId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "SyncError",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "SyncStatus",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "ShopProducts");

            migrationBuilder.DropColumn(
                name: "WeightUnit",
                table: "ShopProducts");
        }
    }
}
