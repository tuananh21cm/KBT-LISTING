using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Rename SHEIN cost columns for long-term clarity:
/// SheinDiscount → SheinCouponPrice, SheinClubSavings → SheinClubDiscount, SheinSalesTax → SheinTax.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723120000_RenameOrderSheinCostColumns")]
public class RenameOrderSheinCostColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "SheinDiscount",
            table: "Orders",
            newName: "SheinCouponPrice");

        migrationBuilder.RenameColumn(
            name: "SheinClubSavings",
            table: "Orders",
            newName: "SheinClubDiscount");

        migrationBuilder.RenameColumn(
            name: "SheinSalesTax",
            table: "Orders",
            newName: "SheinTax");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "SheinCouponPrice",
            table: "Orders",
            newName: "SheinDiscount");

        migrationBuilder.RenameColumn(
            name: "SheinClubDiscount",
            table: "Orders",
            newName: "SheinClubSavings");

        migrationBuilder.RenameColumn(
            name: "SheinTax",
            table: "Orders",
            newName: "SheinSalesTax");
    }
}
