using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Supplier seller + product PDP snapshots; migrate SourceReviewSnapshots then drop.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260805100000_AddSupplierProductInfos")]
public class AddSupplierProductInfos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SupplierSellerInfos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Platform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                StoreCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StoreUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                StoreLogo = table.Column<string>(type: "text", nullable: true),
                StoreTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SoldByTips = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ShipsFromTips = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ShippingDays = table.Column<int>(type: "integer", nullable: true),
                ShippingDayPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                ShippingDayPercentDesc = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SupplierSellerInfos", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SupplierSellerInfos_Platform_StoreCode",
            table: "SupplierSellerInfos",
            columns: ["Platform", "StoreCode"],
            unique: true);

        migrationBuilder.CreateTable(
            name: "SupplierProductInfos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceUrlKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SourceUrl = table.Column<string>(type: "text", nullable: true),
                SupplierSellerInfoId = table.Column<Guid>(type: "uuid", nullable: true),
                Stock = table.Column<int>(type: "integer", nullable: true),
                ReviewCount = table.Column<int>(type: "integer", nullable: true),
                AverageRating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SupplierProductInfos", x => x.Id);
                table.ForeignKey(
                    name: "FK_SupplierProductInfos_SupplierSellerInfos_SupplierSellerInfoId",
                    column: x => x.SupplierSellerInfoId,
                    principalTable: "SupplierSellerInfos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SupplierProductInfos_SourceUrlKey",
            table: "SupplierProductInfos",
            column: "SourceUrlKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SupplierProductInfos_SupplierSellerInfoId",
            table: "SupplierProductInfos",
            column: "SupplierSellerInfoId");

        migrationBuilder.Sql(
            """
            INSERT INTO "SupplierProductInfos" ("Id", "SourceUrlKey", "SourceUrl", "ReviewCount", "AverageRating", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), s."SourceUrlKey", s."SourceUrl", s."ReviewCount", s."AverageRating", s."CreatedAt", s."UpdatedAt"
            FROM "SourceReviewSnapshots" s
            WHERE NOT EXISTS (
                SELECT 1 FROM "SupplierProductInfos" p WHERE p."SourceUrlKey" = s."SourceUrlKey");
            """);

        migrationBuilder.AddColumn<Guid>(
            name: "SupplierProductInfoId",
            table: "Listings",
            type: "uuid",
            nullable: true);

        // Split OR+regex join: a single predicate forces nested-loop × regexp on every
        // Listing × SupplierProductInfo pair and times out Npgsql's 30s command timeout.
        migrationBuilder.Sql(
            """
            UPDATE "Listings" AS l
            SET "SupplierProductInfoId" = p."Id"
            FROM "SupplierProductInfos" AS p
            WHERE l."SupplierProductInfoId" IS NULL
              AND l."SourceUrl" IS NOT NULL
              AND l."SourceUrl" <> ''
              AND p."SourceUrlKey" = 'p:' || substring(l."SourceUrl" from '(?i)-p-(\d+)');
            """);

        migrationBuilder.Sql(
            """
            CREATE INDEX IF NOT EXISTS "IX_tmp_SPI_SourceUrl"
            ON "SupplierProductInfos" ("SourceUrl");
            """);

        migrationBuilder.Sql(
            """
            UPDATE "Listings" AS l
            SET "SupplierProductInfoId" = p."Id"
            FROM "SupplierProductInfos" AS p
            WHERE l."SupplierProductInfoId" IS NULL
              AND l."SourceUrl" IS NOT NULL
              AND l."SourceUrl" <> ''
              AND p."SourceUrl" IS NOT NULL
              AND p."SourceUrl" = l."SourceUrl";
            """);

        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_tmp_SPI_SourceUrl";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Listings_SupplierProductInfoId",
            table: "Listings",
            column: "SupplierProductInfoId");

        migrationBuilder.AddForeignKey(
            name: "FK_Listings_SupplierProductInfos_SupplierProductInfoId",
            table: "Listings",
            column: "SupplierProductInfoId",
            principalTable: "SupplierProductInfos",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.DropTable(name: "SourceReviewSnapshots");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Listings_SupplierProductInfos_SupplierProductInfoId",
            table: "Listings");

        migrationBuilder.DropIndex(
            name: "IX_Listings_SupplierProductInfoId",
            table: "Listings");

        migrationBuilder.DropColumn(
            name: "SupplierProductInfoId",
            table: "Listings");

        migrationBuilder.CreateTable(
            name: "SourceReviewSnapshots",
            columns: table => new
            {
                SourceUrlKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SourceUrl = table.Column<string>(type: "text", nullable: true),
                ReviewCount = table.Column<int>(type: "integer", nullable: true),
                AverageRating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SourceReviewSnapshots", x => x.SourceUrlKey);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO "SourceReviewSnapshots" ("SourceUrlKey", "SourceUrl", "ReviewCount", "AverageRating", "CreatedAt", "UpdatedAt")
            SELECT p."SourceUrlKey", p."SourceUrl", p."ReviewCount", p."AverageRating", p."CreatedAt", p."UpdatedAt"
            FROM "SupplierProductInfos" p
            ON CONFLICT ("SourceUrlKey") DO NOTHING;
            """);

        migrationBuilder.DropTable(name: "SupplierProductInfos");
        migrationBuilder.DropTable(name: "SupplierSellerInfos");
    }
}
