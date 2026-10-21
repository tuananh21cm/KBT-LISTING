using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// One-shot: ghi đè toàn bộ PaidByUserId lịch sử về dinhnguyen
/// (migration trước chỉ fill NULL / giữ buyer từ attempt Purchased).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260926000000_ForcePaidByToDinhnguyen")]
public class ForcePaidByToDinhnguyen : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1) Orders đã mua / có purchase → dinhnguyen (ghi đè mọi buyer khác).
        migrationBuilder.Sql(
            """
            UPDATE "Orders" o
            SET "PaidByUserId" = u."Id"
            FROM "AspNetUsers" u
            WHERE u."NormalizedUserName" = 'DINHNGUYEN'
              AND (
                    o."FulfilPurchasedAt" IS NOT NULL
                 OR EXISTS (SELECT 1 FROM "SupplierPurchases" sp WHERE sp."OrderId" = o."Id")
              )
              AND o."PaidByUserId" IS DISTINCT FROM u."Id";
            """);

        // 2) SupplierPurchases gắn đơn đã có PaidBy → đồng bộ.
        migrationBuilder.Sql(
            """
            UPDATE "SupplierPurchases" sp
            SET "PaidByUserId" = o."PaidByUserId"
            FROM "Orders" o
            WHERE sp."OrderId" = o."Id"
              AND o."PaidByUserId" IS NOT NULL
              AND sp."PaidByUserId" IS DISTINCT FROM o."PaidByUserId";
            """);

        // 3) Mọi FulfilTransactions còn khác / null → dinhnguyen.
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" t
            SET "PaidByUserId" = u."Id"
            FROM "AspNetUsers" u
            WHERE u."NormalizedUserName" = 'DINHNGUYEN'
              AND t."PaidByUserId" IS DISTINCT FROM u."Id";
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // One-shot data fix — không khôi phục buyer cũ.
    }
}
