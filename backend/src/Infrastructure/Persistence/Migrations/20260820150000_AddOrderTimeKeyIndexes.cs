using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Expression indexes for the Orders list.
///
/// The list filters and sorts on <c>COALESCE(OrderPaidTime, OrderCreateTime, CreatedAt)</c>.
/// Existing indexes only cover the raw <c>OrderPaidTime</c> column, so every page had to sort
/// the whole filtered set. Also indexes <c>LOWER(SellerSku)</c> on ListingVariations, which the
/// line-item listing overlay joins on and which previously had no index at all.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260820150000_AddOrderTimeKeyIndexes")]
public class AddOrderTimeKeyIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Raw SQL: EF cannot express indexes over a COALESCE expression.
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_OrderTimeKey_Desc"
              ON "Orders" ((COALESCE("OrderPaidTime", "OrderCreateTime", "CreatedAt")) DESC);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_Owner_OrderTimeKey_Desc"
              ON "Orders" ("OwnerId", (COALESCE("OrderPaidTime", "OrderCreateTime", "CreatedAt")) DESC);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_ListingVariations_SellerSkuLower"
              ON "ListingVariations" (LOWER("SellerSku"))
              WHERE "SellerSku" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_ListingVariations_SellerSkuLower";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Orders_Owner_OrderTimeKey_Desc";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Orders_OrderTimeKey_Desc";""");
    }
}
