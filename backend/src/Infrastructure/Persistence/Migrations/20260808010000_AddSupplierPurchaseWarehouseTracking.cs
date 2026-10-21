using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Preserve SHEIN warehouse C…/N… when TrackingNo upgrades to last-mile carrier.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260808010000_AddSupplierPurchaseWarehouseTracking")]
public class AddSupplierPurchaseWarehouseTracking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "WarehouseTrackingNo",
            table: "SupplierPurchases",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "WarehouseTrackingSeenAt",
            table: "SupplierPurchases",
            type: "timestamp with time zone",
            nullable: true);

        // Backfill from current TrackingNo while still warehouse-shaped.
        migrationBuilder.Sql(
            """
            UPDATE "SupplierPurchases"
            SET "WarehouseTrackingNo" = "TrackingNo",
                "WarehouseTrackingSeenAt" = COALESCE("UpdatedAt", "CreatedAt")
            WHERE "WarehouseTrackingNo" IS NULL
              AND "TrackingNo" IS NOT NULL
              AND (
                "TrackingKind" = 'warehouse'
                OR "TrackingNo" ~* '^[CN][0-9]{10,}$'
              );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "WarehouseTrackingNo",
            table: "SupplierPurchases");

        migrationBuilder.DropColumn(
            name: "WarehouseTrackingSeenAt",
            table: "SupplierPurchases");
    }
}
