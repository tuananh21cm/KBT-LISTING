using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Index cho khớp billno theo dấu «Đã mua» + backfill PaidByUserId từ attempt Purchased,
/// fallback dinhnguyen cho đơn đã mua / giao dịch còn trống.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260925200000_BackfillPaidByFromPurchaseMarks")]
public class BackfillPaidByFromPurchaseMarks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_OrderFulfilAttempts_CustomerKey_CompletedAt",
            table: "OrderFulfilAttempts",
            columns: new[] { "CustomerKey", "CompletedAt" },
            descending: new[] { false, true },
            filter: "\"Outcome\" = 'Purchased' AND \"CustomerKey\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_OrderFulfilAttempts_OrderId_Outcome",
            table: "OrderFulfilAttempts",
            columns: new[] { "OrderId", "Outcome" });

        // Bảng evidence: đơn được suy từ bước 2-3 (bằng chứng thật) — lan xuống purchase/txn
        // đang trỏ user khác. Fallback dinhnguyen (bước 4) không vào đây.
        migrationBuilder.Sql(
            """
            CREATE TEMP TABLE "_PaidByEvidenceOrders" (
                "Id" uuid PRIMARY KEY
            );
            """);

        // Bước 2 — bằng chứng trực tiếp: attempt Purchased mới nhất trên chính đơn.
        migrationBuilder.Sql(
            """
            WITH chosen AS (
                SELECT DISTINCT ON ("OrderId") "OrderId", "UserId"
                FROM "OrderFulfilAttempts"
                WHERE "Outcome" = 'Purchased'
                ORDER BY "OrderId", "CompletedAt" DESC NULLS LAST, "ClosedAt" DESC NULLS LAST
            ),
            updated AS (
                UPDATE "Orders" o
                SET "PaidByUserId" = a."UserId"
                FROM chosen a
                WHERE o."Id" = a."OrderId"
                RETURNING o."Id"
            )
            INSERT INTO "_PaidByEvidenceOrders" ("Id")
            SELECT "Id" FROM updated
            ON CONFLICT DO NOTHING;
            """);

        // Bước 3 — bằng chứng nhóm khách: đơn còn NULL, attempt Purchased cùng CustomerKey ±24h
        // quanh SupplierPaidAt của billno gắn trên đơn.
        migrationBuilder.Sql(
            """
            WITH sub AS (
                SELECT DISTINCT ON (o2."Id")
                    o2."Id" AS "OrderId",
                    a."UserId"
                FROM "Orders" o2
                INNER JOIN "SupplierPurchases" sp ON sp."OrderId" = o2."Id"
                INNER JOIN "OrderFulfilAttempts" a
                    ON a."Outcome" = 'Purchased'
                    AND a."CustomerKey" IS NOT NULL
                    AND a."CompletedAt" IS NOT NULL
                    AND a."CustomerKey" = (
                        CASE
                            WHEN NULLIF(regexp_replace(COALESCE(o2."BuyerPhone", ''), '[^0-9]', '', 'g'), '') IS NOT NULL
                             AND NULLIF(regexp_replace(COALESCE(o2."PostalCode", ''), '[^0-9]', '', 'g'), '') IS NOT NULL
                            THEN regexp_replace(COALESCE(o2."BuyerPhone", ''), '[^0-9]', '', 'g')
                                 || '|'
                                 || regexp_replace(COALESCE(o2."PostalCode", ''), '[^0-9]', '', 'g')
                            ELSE NULL
                        END
                    )
                    AND a."CompletedAt" BETWEEN
                        COALESCE(sp."SupplierPaidAt", sp."OrderedAt", sp."CreatedAt") - INTERVAL '24 hours'
                        AND COALESCE(sp."SupplierPaidAt", sp."OrderedAt", sp."CreatedAt") + INTERVAL '24 hours'
                WHERE o2."PaidByUserId" IS NULL
                ORDER BY o2."Id",
                    ABS(EXTRACT(EPOCH FROM (a."CompletedAt" - COALESCE(sp."SupplierPaidAt", sp."OrderedAt", sp."CreatedAt"))))
            ),
            updated AS (
                UPDATE "Orders" o
                SET "PaidByUserId" = sub."UserId"
                FROM sub
                WHERE o."Id" = sub."OrderId"
                  AND o."PaidByUserId" IS NULL
                RETURNING o."Id"
            )
            INSERT INTO "_PaidByEvidenceOrders" ("Id")
            SELECT "Id" FROM updated
            ON CONFLICT DO NOTHING;
            """);

        // Bước 4 — fallback dinhnguyen cho đơn đã mua còn trống (guard khi user không tồn tại).
        migrationBuilder.Sql(
            """
            UPDATE "Orders" o
            SET "PaidByUserId" = u."Id"
            FROM "AspNetUsers" u
            WHERE u."NormalizedUserName" = 'DINHNGUYEN'
              AND o."PaidByUserId" IS NULL
              AND (
                    o."FulfilPurchasedAt" IS NOT NULL
                 OR EXISTS (SELECT 1 FROM "SupplierPurchases" sp WHERE sp."OrderId" = o."Id")
              );
            """);

        // Bước 5 — lan xuống SupplierPurchases.
        // NULL → lấy của đơn. Đang trỏ user khác chỉ ghi đè khi đơn thuộc evidence (bước 2-3).
        migrationBuilder.Sql(
            """
            UPDATE "SupplierPurchases" sp
            SET "PaidByUserId" = o."PaidByUserId"
            FROM "Orders" o
            WHERE sp."OrderId" = o."Id"
              AND o."PaidByUserId" IS NOT NULL
              AND (
                    sp."PaidByUserId" IS NULL
                 OR (
                        sp."PaidByUserId" IS DISTINCT FROM o."PaidByUserId"
                    AND EXISTS (SELECT 1 FROM "_PaidByEvidenceOrders" e WHERE e."Id" = o."Id")
                 )
              );
            """);

        // Bước 6a — lan CostUpdate theo cùng luật bước 5.
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" t
            SET "PaidByUserId" = o."PaidByUserId"
            FROM "Orders" o
            WHERE t."OrderId" = o."Id"
              AND t."Source" = 'CostUpdate'
              AND o."PaidByUserId" IS NOT NULL
              AND (
                    t."PaidByUserId" IS NULL
                 OR (
                        t."PaidByUserId" IS DISTINCT FROM o."PaidByUserId"
                    AND EXISTS (SELECT 1 FROM "_PaidByEvidenceOrders" e WHERE e."Id" = o."Id")
                 )
              );
            """);

        // Bước 6b — mọi FulfilTransactions còn NULL → dinhnguyen (guard).
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" t
            SET "PaidByUserId" = u."Id"
            FROM "AspNetUsers" u
            WHERE u."NormalizedUserName" = 'DINHNGUYEN'
              AND t."PaidByUserId" IS NULL;
            """);

        migrationBuilder.Sql("""DROP TABLE IF EXISTS "_PaidByEvidenceOrders";""");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrderFulfilAttempts_CustomerKey_CompletedAt",
            table: "OrderFulfilAttempts");

        migrationBuilder.DropIndex(
            name: "IX_OrderFulfilAttempts_OrderId_Outcome",
            table: "OrderFulfilAttempts");
    }
}
