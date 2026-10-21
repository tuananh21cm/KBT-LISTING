using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Remembers the 4Seller shop id a shop answered to before it was reconnected under another
/// 4Seller account, so ownership rematch still finds Orders / Listings carrying the old id.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260915100000_AddShopPreviousFourSellerShopId")]
public class AddShopPreviousFourSellerShopId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PreviousFourSellerShopId",
            table: "Shops",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PreviousFourSellerShopId",
            table: "Shops");
    }
}
