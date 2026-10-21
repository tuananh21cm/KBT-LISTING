using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Speeds auto-publish scheduler EXISTS checks on Running runs by ShopId + StartedAt.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915223000_AddListingAutoPublishRunStatusShopStartedIndex")]
public class AddListingAutoPublishRunStatusShopStartedIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_ListingAutoPublishRuns_Status_ShopId_StartedAt",
            table: "ListingAutoPublishRuns",
            columns: new[] { "Status", "ShopId", "StartedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ListingAutoPublishRuns_Status_ShopId_StartedAt",
            table: "ListingAutoPublishRuns");
    }
}
