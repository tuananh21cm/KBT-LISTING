using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Local-only jsonb for last applied batch pricing formula on Listings.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260727120000_AddListingAppliedPricing")]
public class AddListingAppliedPricing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AppliedPricingJson",
            table: "Listings",
            type: "jsonb",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AppliedPricingJson",
            table: "Listings");
    }
}
