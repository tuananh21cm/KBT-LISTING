using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260722090000_AddOrderFulfilMilestones")]
public class AddOrderFulfilMilestones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "FulfilPurchasedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TrackingAddedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "InTransitAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "DeliveredAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "RefundedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "CanceledAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_FulfilPurchasedAt",
            table: "Orders",
            column: "FulfilPurchasedAt");

        migrationBuilder.CreateIndex(
            name: "IX_Orders_DeliveredAt",
            table: "Orders",
            column: "DeliveredAt");

        // Backfill tracking / cancel / fulfil purchase from existing signals.
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "TrackingAddedAt" = COALESCE("OrderShippedTime", "LastSyncedAt", "UpdatedAt", "CreatedAt")
            WHERE "TrackingNo" IS NOT NULL
              AND BTRIM("TrackingNo") <> ''
              AND "TrackingAddedAt" IS NULL;

            UPDATE "Orders"
            SET "CanceledAt" = COALESCE("UpdatedAt", "LastSyncedAt", "CreatedAt")
            WHERE LOWER("OrderStatus") IN ('canceled', 'cancelled')
              AND "CanceledAt" IS NULL;

            UPDATE "Orders" o
            SET "FulfilPurchasedAt" = t."OccurredAt"
            FROM "FulfilTransactions" t
            WHERE t."OrderId" = o."Id"
              AND t."Source" = 'CostUpdate'
              AND o."FulfilPurchasedAt" IS NULL
              AND o."FulfilAccountId" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Orders_FulfilPurchasedAt",
            table: "Orders");

        migrationBuilder.DropIndex(
            name: "IX_Orders_DeliveredAt",
            table: "Orders");

        migrationBuilder.DropColumn(name: "FulfilPurchasedAt", table: "Orders");
        migrationBuilder.DropColumn(name: "TrackingAddedAt", table: "Orders");
        migrationBuilder.DropColumn(name: "InTransitAt", table: "Orders");
        migrationBuilder.DropColumn(name: "DeliveredAt", table: "Orders");
        migrationBuilder.DropColumn(name: "RefundedAt", table: "Orders");
        migrationBuilder.DropColumn(name: "CanceledAt", table: "Orders");
    }
}
