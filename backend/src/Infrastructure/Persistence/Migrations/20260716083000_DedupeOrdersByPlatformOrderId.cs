using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Removes duplicate local orders that share the same owner + platform order code,
/// then enforces a filtered unique index on (OwnerId, PlatformOrderId).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260716083000_DedupeOrdersByPlatformOrderId")]
public class DedupeOrdersByPlatformOrderId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Keep the newest row per (OwnerId, PlatformOrderId); cascade-delete the rest via OrderItems FK.
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "PlatformOrderId" = btrim("PlatformOrderId")
            WHERE "PlatformOrderId" IS NOT NULL
              AND "PlatformOrderId" <> btrim("PlatformOrderId");

            UPDATE "Orders"
            SET "PlatformOrderId" = NULL
            WHERE "PlatformOrderId" IS NOT NULL
              AND btrim("PlatformOrderId") = '';

            WITH ranked AS (
                SELECT "Id",
                       ROW_NUMBER() OVER (
                           PARTITION BY "OwnerId", "PlatformOrderId"
                           ORDER BY "LastSyncedAt" DESC NULLS LAST,
                                    "UpdatedAt" DESC NULLS LAST,
                                    "CreatedAt" DESC
                       ) AS rn
                FROM "Orders"
                WHERE "PlatformOrderId" IS NOT NULL
                  AND btrim("PlatformOrderId") <> ''
            )
            DELETE FROM "OrderItems"
            WHERE "OrderId" IN (SELECT "Id" FROM ranked WHERE rn > 1);

            WITH ranked AS (
                SELECT "Id",
                       ROW_NUMBER() OVER (
                           PARTITION BY "OwnerId", "PlatformOrderId"
                           ORDER BY "LastSyncedAt" DESC NULLS LAST,
                                    "UpdatedAt" DESC NULLS LAST,
                                    "CreatedAt" DESC
                       ) AS rn
                FROM "Orders"
                WHERE "PlatformOrderId" IS NOT NULL
                  AND btrim("PlatformOrderId") <> ''
            )
            DELETE FROM "Orders"
            WHERE "Id" IN (SELECT "Id" FROM ranked WHERE rn > 1);
            """);

        migrationBuilder.DropIndex(
            name: "IX_Orders_PlatformOrderId",
            table: "Orders");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_OwnerId_PlatformOrderId",
            table: "Orders",
            columns: new[] { "OwnerId", "PlatformOrderId" },
            unique: true,
            filter: "\"PlatformOrderId\" IS NOT NULL AND \"PlatformOrderId\" <> ''");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Orders_OwnerId_PlatformOrderId",
            table: "Orders");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_PlatformOrderId",
            table: "Orders",
            column: "PlatformOrderId");
    }
}
