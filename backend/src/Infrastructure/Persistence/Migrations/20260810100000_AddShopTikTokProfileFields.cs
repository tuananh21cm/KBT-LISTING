using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260810100000_AddShopTikTokProfileFields")]
public class AddShopTikTokProfileFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ShopCode",
            table: "Shops",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "TikTokShopStatus",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Timezone",
            table: "Shops",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TikTokCreatedAt",
            table: "Shops",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "VerifiedDocuments",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "EmailVerified",
            table: "Shops",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "PhoneVerified",
            table: "Shops",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "SellerType",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BusinessType",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PayEntityType",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CompanyCertNum",
            table: "Shops",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CompanyCertType",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AccountName",
            table: "Shops",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsBlocked",
            table: "Shops",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsBlocking",
            table: "Shops",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PublishLimitPublished",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PublishLimitTotal",
            table: "Shops",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TikTokProfileSyncedAt",
            table: "Shops",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CompanyAddress",
            table: "Shops",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OwnerPerson",
            table: "Shops",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LogisticsServices",
            table: "Shops",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProductCategories",
            table: "Shops",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShopCode", table: "Shops");
        migrationBuilder.DropColumn(name: "TikTokShopStatus", table: "Shops");
        migrationBuilder.DropColumn(name: "Timezone", table: "Shops");
        migrationBuilder.DropColumn(name: "TikTokCreatedAt", table: "Shops");
        migrationBuilder.DropColumn(name: "VerifiedDocuments", table: "Shops");
        migrationBuilder.DropColumn(name: "EmailVerified", table: "Shops");
        migrationBuilder.DropColumn(name: "PhoneVerified", table: "Shops");
        migrationBuilder.DropColumn(name: "SellerType", table: "Shops");
        migrationBuilder.DropColumn(name: "BusinessType", table: "Shops");
        migrationBuilder.DropColumn(name: "PayEntityType", table: "Shops");
        migrationBuilder.DropColumn(name: "CompanyCertNum", table: "Shops");
        migrationBuilder.DropColumn(name: "CompanyCertType", table: "Shops");
        migrationBuilder.DropColumn(name: "AccountName", table: "Shops");
        migrationBuilder.DropColumn(name: "IsBlocked", table: "Shops");
        migrationBuilder.DropColumn(name: "IsBlocking", table: "Shops");
        migrationBuilder.DropColumn(name: "PublishLimitPublished", table: "Shops");
        migrationBuilder.DropColumn(name: "PublishLimitTotal", table: "Shops");
        migrationBuilder.DropColumn(name: "TikTokProfileSyncedAt", table: "Shops");
        migrationBuilder.DropColumn(name: "CompanyAddress", table: "Shops");
        migrationBuilder.DropColumn(name: "OwnerPerson", table: "Shops");
        migrationBuilder.DropColumn(name: "LogisticsServices", table: "Shops");
        migrationBuilder.DropColumn(name: "ProductCategories", table: "Shops");
    }
}
