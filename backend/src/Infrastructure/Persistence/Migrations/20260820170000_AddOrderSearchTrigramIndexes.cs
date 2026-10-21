using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Trigram indexes for the orders search box.
///
/// The search runs <c>Contains</c> on several columns, which is <c>LIKE '%term%'</c> — a leading
/// wildcard, so a plain B-tree index cannot serve it and every keystroke meant a sequential scan.
/// <c>pg_trgm</c> GIN indexes are the one index type Postgres can use for that pattern.
///
/// Only the columns the search actually routes to are indexed: the code columns plus buyer name and
/// address. City / state / postal stay unindexed — they are cheap equality-ish fields and adding GIN
/// there would cost write throughput for no measurable read gain.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260820170000_AddOrderSearchTrigramIndexes")]
public class AddOrderSearchTrigramIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_PlatformOrderId_Trgm"
              ON "Orders" USING gin ("PlatformOrderId" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_TrackingNo_Trgm"
              ON "Orders" USING gin ("TrackingNo" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_ShipmentNo_Trgm"
              ON "Orders" USING gin ("ShipmentNo" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_BuyerPhone_Trgm"
              ON "Orders" USING gin ("BuyerPhone" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_BuyerName_Trgm"
              ON "Orders" USING gin ("BuyerName" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_Orders_Address_Trgm"
              ON "Orders" USING gin ("Address" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_SupplierPurchases_SupplierOrderNo_Trgm"
              ON "SupplierPurchases" USING gin ("SupplierOrderNo" gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_SupplierPurchases_TrackingNo_Trgm"
              ON "SupplierPurchases" USING gin ("TrackingNo" gin_trgm_ops);
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[]
        {
            "IX_SupplierPurchases_TrackingNo_Trgm",
            "IX_SupplierPurchases_SupplierOrderNo_Trgm",
            "IX_Orders_Address_Trgm",
            "IX_Orders_BuyerName_Trgm",
            "IX_Orders_BuyerPhone_Trgm",
            "IX_Orders_ShipmentNo_Trgm",
            "IX_Orders_TrackingNo_Trgm",
            "IX_Orders_PlatformOrderId_Trgm"
        })
        {
            migrationBuilder.Sql($"DROP INDEX IF EXISTS \"{name}\";");
        }

        // pg_trgm is left installed: other features may rely on it and dropping an extension is
        // not something a schema rollback should decide.
    }
}
