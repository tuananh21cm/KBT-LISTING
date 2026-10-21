using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Splits Sync Order (4Seller) from Sync Fulfil (SHEIN): adds per-milestone provenance and repairs
/// timeline values that were stamped with sync-run time (which produced inverted "—" and zero "&lt;1m"
/// gaps in the UI). Clears the clearly-invalid soft milestones and marks every surviving value as
/// <c>legacy</c> so a follow-up SHEIN sync (mode=all) overwrites them with real event clocks.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726210000_RepairFulfilTimeline")]
public class RepairFulfilTimeline : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FulfilTimelineSources",
            table: "Orders",
            type: "jsonb",
            nullable: true);

        // 1. Clear inverted / same-sync in-transit stamps — the direct cause of Transit→Giao "—"
        //    and "<1m": InTransit at/after Delivered, within 60s of Delivered (one sync run), or
        //    earlier than Track. SHEIN re-sync re-stamps from the real ship clock.
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "InTransitAt" = NULL
            WHERE "InTransitAt" IS NOT NULL
              AND (
                ("DeliveredAt" IS NOT NULL AND "InTransitAt" >= "DeliveredAt")
                OR ("DeliveredAt" IS NOT NULL AND "DeliveredAt" - "InTransitAt" < INTERVAL '60 seconds')
                OR ("TrackingAddedAt" IS NOT NULL AND "InTransitAt" < "TrackingAddedAt")
              );
            """);

        // 2. Tracking cannot land at/after delivery.
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "TrackingAddedAt" = NULL
            WHERE "TrackingAddedAt" IS NOT NULL
              AND "DeliveredAt" IS NOT NULL
              AND "TrackingAddedAt" >= "DeliveredAt";
            """);

        // 3. Sync-time fingerprints: mốc nằm trong 60s của UpdatedAt / LastSyncedAt (hoặc trùng
        //    CreatedAt từ backfill 20260722090000) mà không khớp OrderShippedTime thật.
        //    DeliveredAt / InTransitAt từ 4Seller `??= now` cũng thường khớp UpdatedAt.
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "TrackingAddedAt" = NULL
            WHERE "TrackingAddedAt" IS NOT NULL
              AND (
                "OrderShippedTime" IS NULL
                OR ABS(EXTRACT(EPOCH FROM ("TrackingAddedAt" - "OrderShippedTime"))) > 60
              )
              AND (
                ABS(EXTRACT(EPOCH FROM ("TrackingAddedAt" - "UpdatedAt"))) < 60
                OR ABS(EXTRACT(EPOCH FROM ("TrackingAddedAt" - "LastSyncedAt"))) < 60
                OR ABS(EXTRACT(EPOCH FROM ("TrackingAddedAt" - "CreatedAt"))) < 60
              );

            UPDATE "Orders"
            SET "InTransitAt" = NULL
            WHERE "InTransitAt" IS NOT NULL
              AND (
                ABS(EXTRACT(EPOCH FROM ("InTransitAt" - "UpdatedAt"))) < 60
                OR ABS(EXTRACT(EPOCH FROM ("InTransitAt" - "LastSyncedAt"))) < 60
              );

            UPDATE "Orders"
            SET "DeliveredAt" = NULL
            WHERE "DeliveredAt" IS NOT NULL
              AND (
                ABS(EXTRACT(EPOCH FROM ("DeliveredAt" - "UpdatedAt"))) < 60
                OR ABS(EXTRACT(EPOCH FROM ("DeliveredAt" - "LastSyncedAt"))) < 60
              );

            -- Backfill CanceledAt từ UpdatedAt/CreatedAt (migration 20260722090000) khi không
            -- có SupplierStatusAt cancel/refund tương ứng.
            UPDATE "Orders" o
            SET "CanceledAt" = NULL
            WHERE o."CanceledAt" IS NOT NULL
              AND (
                ABS(EXTRACT(EPOCH FROM (o."CanceledAt" - o."UpdatedAt"))) < 60
                OR ABS(EXTRACT(EPOCH FROM (o."CanceledAt" - o."LastSyncedAt"))) < 60
                OR ABS(EXTRACT(EPOCH FROM (o."CanceledAt" - o."CreatedAt"))) < 60
              )
              AND NOT EXISTS (
                SELECT 1 FROM "OrderSupplierLinks" l
                WHERE l."OrderId" = o."Id"
                  AND l."SupplierStatusAt" IS NOT NULL
                  AND ABS(EXTRACT(EPOCH FROM (l."SupplierStatusAt" - o."CanceledAt"))) < 120
                  AND LOWER(COALESCE(l."SupplierStatus", '')) IN ('canceled', 'cancelled', 'refunded')
              );
            """);

        // 4. Mark every surviving milestone as `legacy` provenance so the next SHEIN sync
        //    (rank shein > legacy) overwrites it with a real clock; Manual edits still win.
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "FulfilTimelineSources" = jsonb_strip_nulls(jsonb_build_object(
                'fulfilPurchasedAt', CASE WHEN "FulfilPurchasedAt" IS NOT NULL THEN to_jsonb('legacy'::text) END,
                'trackingAddedAt',   CASE WHEN "TrackingAddedAt"   IS NOT NULL THEN to_jsonb('legacy'::text) END,
                'inTransitAt',       CASE WHEN "InTransitAt"       IS NOT NULL THEN to_jsonb('legacy'::text) END,
                'deliveredAt',       CASE WHEN "DeliveredAt"       IS NOT NULL THEN to_jsonb('legacy'::text) END,
                'refundedAt',        CASE WHEN "RefundedAt"        IS NOT NULL THEN to_jsonb('legacy'::text) END,
                'canceledAt',        CASE WHEN "CanceledAt"        IS NOT NULL THEN to_jsonb('legacy'::text) END
            ))
            WHERE "FulfilPurchasedAt" IS NOT NULL
               OR "TrackingAddedAt" IS NOT NULL
               OR "InTransitAt" IS NOT NULL
               OR "DeliveredAt" IS NOT NULL
               OR "RefundedAt" IS NOT NULL
               OR "CanceledAt" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Cleared milestone values cannot be restored; only the provenance column is reversible.
        migrationBuilder.DropColumn(name: "FulfilTimelineSources", table: "Orders");
    }
}
