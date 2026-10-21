using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260719190000_AddListingListIndexes")]
public class AddListingListIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Listings_PageType_PlatformUpdateTime",
            table: "Listings",
            columns: new[] { "PageType", "PlatformUpdateTime" });

        migrationBuilder.CreateIndex(
            name: "IX_Listings_OwnerId_PageType_SiteStatus",
            table: "Listings",
            columns: new[] { "OwnerId", "PageType", "SiteStatus" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Listings_OwnerId_PageType_SiteStatus",
            table: "Listings");

        migrationBuilder.DropIndex(
            name: "IX_Listings_PageType_PlatformUpdateTime",
            table: "Listings");
    }
}
