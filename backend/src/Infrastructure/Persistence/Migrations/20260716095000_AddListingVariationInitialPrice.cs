using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716095000_AddListingVariationInitialPrice")]
public class AddListingVariationInitialPrice : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "InitialPrice",
            table: "ListingVariations",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "ListingVariations"
            SET "InitialPrice" = "SalePrice"
            WHERE "InitialPrice" IS NULL
              AND "SalePrice" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "InitialPrice", table: "ListingVariations");
    }
}
