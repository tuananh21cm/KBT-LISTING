using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Moves the auto CostUpdate ledger from one row per local order to one row per supplier
/// billno, so each SHEIN card charge can be reconciled 1-1 against a card statement.
/// Existing merged rows are split by each payable link's own <c>SupplierTotal</c>; the total
/// debited per order is unchanged.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260801170000_SplitCostUpdateLedgerPerBillno")]
public class SplitCostUpdateLedgerPerBillno : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "OrderSupplierLinkId",
            table: "FulfilTransactions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "FulfilAccountId",
            table: "OrderSupplierLinks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "FulfilCardId",
            table: "OrderSupplierLinks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "PaidByUserId",
            table: "OrderSupplierLinks",
            type: "uuid",
            nullable: true);

        // Must drop before the backfill: splitting produces several CostUpdate rows per order.
        migrationBuilder.DropIndex(
            name: "IX_FulfilTransactions_OrderId_CostUpdate",
            table: "FulfilTransactions");

        // Per-billno payment metadata inherited from the parent order.
        migrationBuilder.Sql("""
            UPDATE "OrderSupplierLinks" l
            SET "FulfilAccountId" = o."FulfilAccountId",
                "FulfilCardId" = o."FulfilCardId",
                "PaidByUserId" = o."PaidByUserId"
            FROM "Orders" o
            WHERE o."Id" = l."OrderId"
              AND (o."FulfilAccountId" IS NOT NULL
                   OR o."FulfilCardId" IS NOT NULL
                   OR o."PaidByUserId" IS NOT NULL);
            """);

        // Point each existing CostUpdate row at the oldest payable link of its order. Only
        // orders that actually need splitting get their amount rewritten — a single-billno
        // order keeps whatever an admin may have corrected by hand.
        migrationBuilder.Sql("""
            WITH payable AS (
                SELECT l."Id",
                       l."OrderId",
                       l."SupplierTotal",
                       l."OrderedAt",
                       ROW_NUMBER() OVER (
                           PARTITION BY l."OrderId"
                           ORDER BY l."CreatedAt", l."Id"
                       ) AS rn,
                       COUNT(*) OVER (PARTITION BY l."OrderId") AS cnt
                FROM "OrderSupplierLinks" l
                WHERE l."SupplierTotal" IS NOT NULL
                  AND (l."SupplierStatus" IS NULL
                       OR l."SupplierStatus" NOT IN ('canceled', 'refunded'))
            )
            UPDATE "FulfilTransactions" t
            SET "OrderSupplierLinkId" = p."Id",
                "Amount" = CASE WHEN p.cnt > 1 THEN p."SupplierTotal" ELSE t."Amount" END,
                "OccurredAt" = CASE
                    WHEN p.cnt > 1 THEN COALESCE(p."OrderedAt", t."OccurredAt")
                    ELSE t."OccurredAt"
                END,
                "UpdatedAt" = NOW() AT TIME ZONE 'UTC'
            FROM payable p
            WHERE p."OrderId" = t."OrderId"
              AND p.rn = 1
              AND t."Source" = 'CostUpdate';
            """);

        // One new ledger row for every remaining payable link, cloning the payment metadata
        // of the order's original CostUpdate row.
        migrationBuilder.Sql("""
            WITH payable AS (
                SELECT l."Id",
                       l."OrderId",
                       l."SupplierTotal",
                       l."OrderedAt",
                       l."SupplierOrderNo",
                       l."FulfilAccountId",
                       l."FulfilCardId",
                       l."PaidByUserId",
                       ROW_NUMBER() OVER (
                           PARTITION BY l."OrderId"
                           ORDER BY l."CreatedAt", l."Id"
                       ) AS rn
                FROM "OrderSupplierLinks" l
                WHERE l."SupplierTotal" IS NOT NULL
                  AND (l."SupplierStatus" IS NULL
                       OR l."SupplierStatus" NOT IN ('canceled', 'refunded'))
            )
            INSERT INTO "FulfilTransactions" (
                "Id", "FulfilAccountId", "FulfilCardId", "OrderId", "OrderSupplierLinkId",
                "Amount", "Direction", "Currency", "Note", "Source",
                "PaidByUserId", "CreatedByUserId", "OccurredAt", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(),
                   COALESCE(p."FulfilAccountId", t."FulfilAccountId"),
                   COALESCE(p."FulfilCardId", t."FulfilCardId"),
                   t."OrderId",
                   p."Id",
                   p."SupplierTotal",
                   'Debit',
                   t."Currency",
                   'Cập nhật phí đơn NCC ' || p."SupplierOrderNo",
                   'CostUpdate',
                   COALESCE(p."PaidByUserId", t."PaidByUserId"),
                   t."CreatedByUserId",
                   COALESCE(p."OrderedAt", t."OccurredAt"),
                   NOW() AT TIME ZONE 'UTC',
                   NOW() AT TIME ZONE 'UTC'
            FROM payable p
            JOIN "FulfilTransactions" t
              ON t."OrderId" = p."OrderId"
             AND t."Source" = 'CostUpdate'
             AND t."OrderSupplierLinkId" IS NOT NULL
             AND t."OrderSupplierLinkId" <> p."Id"
            WHERE p.rn > 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_LinkId_CostUpdate",
            table: "FulfilTransactions",
            column: "OrderSupplierLinkId",
            unique: true,
            filter: "\"OrderSupplierLinkId\" IS NOT NULL AND \"Source\" = 'CostUpdate'");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_OrderId_CostUpdate",
            table: "FulfilTransactions",
            column: "OrderId",
            unique: true,
            filter: "\"OrderId\" IS NOT NULL AND \"OrderSupplierLinkId\" IS NULL AND \"Source\" = 'CostUpdate'");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_OrderSupplierLinkId",
            table: "FulfilTransactions",
            column: "OrderSupplierLinkId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_FulfilAccountId",
            table: "OrderSupplierLinks",
            column: "FulfilAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_FulfilCardId",
            table: "OrderSupplierLinks",
            column: "FulfilCardId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderSupplierLinks_PaidByUserId",
            table: "OrderSupplierLinks",
            column: "PaidByUserId");

        migrationBuilder.AddForeignKey(
            name: "FK_FulfilTransactions_OrderSupplierLinks_OrderSupplierLinkId",
            table: "FulfilTransactions",
            column: "OrderSupplierLinkId",
            principalTable: "OrderSupplierLinks",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_OrderSupplierLinks_FulfilAccounts_FulfilAccountId",
            table: "OrderSupplierLinks",
            column: "FulfilAccountId",
            principalTable: "FulfilAccounts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_OrderSupplierLinks_FulfilCards_FulfilCardId",
            table: "OrderSupplierLinks",
            column: "FulfilCardId",
            principalTable: "FulfilCards",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_OrderSupplierLinks_AspNetUsers_PaidByUserId",
            table: "OrderSupplierLinks",
            column: "PaidByUserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_OrderSupplierLinks_AspNetUsers_PaidByUserId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropForeignKey(
            name: "FK_OrderSupplierLinks_FulfilCards_FulfilCardId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropForeignKey(
            name: "FK_OrderSupplierLinks_FulfilAccounts_FulfilAccountId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropForeignKey(
            name: "FK_FulfilTransactions_OrderSupplierLinks_OrderSupplierLinkId",
            table: "FulfilTransactions");

        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_PaidByUserId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_FulfilCardId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropIndex(
            name: "IX_OrderSupplierLinks_FulfilAccountId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropIndex(
            name: "IX_FulfilTransactions_OrderSupplierLinkId",
            table: "FulfilTransactions");

        migrationBuilder.DropIndex(
            name: "IX_FulfilTransactions_OrderId_CostUpdate",
            table: "FulfilTransactions");

        migrationBuilder.DropIndex(
            name: "IX_FulfilTransactions_LinkId_CostUpdate",
            table: "FulfilTransactions");

        // Re-merge: keep the oldest CostUpdate row per order carrying the summed amount.
        migrationBuilder.Sql("""
            WITH keeper AS (
                SELECT DISTINCT ON ("OrderId") "Id", "OrderId"
                FROM "FulfilTransactions"
                WHERE "Source" = 'CostUpdate' AND "OrderId" IS NOT NULL
                ORDER BY "OrderId", "CreatedAt", "Id"
            ),
            totals AS (
                SELECT "OrderId", SUM("Amount") AS total
                FROM "FulfilTransactions"
                WHERE "Source" = 'CostUpdate' AND "OrderId" IS NOT NULL
                GROUP BY "OrderId"
            )
            UPDATE "FulfilTransactions" t
            SET "Amount" = totals.total,
                "OrderSupplierLinkId" = NULL,
                "UpdatedAt" = NOW() AT TIME ZONE 'UTC'
            FROM keeper, totals
            WHERE keeper."Id" = t."Id" AND totals."OrderId" = t."OrderId";
            """);

        migrationBuilder.Sql("""
            DELETE FROM "FulfilTransactions"
            WHERE "Source" = 'CostUpdate' AND "OrderSupplierLinkId" IS NOT NULL;
            """);

        migrationBuilder.DropColumn(
            name: "PaidByUserId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "FulfilCardId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "FulfilAccountId",
            table: "OrderSupplierLinks");

        migrationBuilder.DropColumn(
            name: "OrderSupplierLinkId",
            table: "FulfilTransactions");

        migrationBuilder.CreateIndex(
            name: "IX_FulfilTransactions_OrderId_CostUpdate",
            table: "FulfilTransactions",
            column: "OrderId",
            unique: true,
            filter: "\"OrderId\" IS NOT NULL AND \"Source\" = 'CostUpdate'");
    }
}
