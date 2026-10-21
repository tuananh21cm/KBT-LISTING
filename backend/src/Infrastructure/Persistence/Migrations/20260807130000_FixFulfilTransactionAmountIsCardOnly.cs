using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807130000_FixFulfilTransactionAmountIsCardOnly")]
public class FixFulfilTransactionAmountIsCardOnly : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Số tiền = totalPrice hóa đơn / thẻ — không cộng ví (ví nằm ở WalletAmount).
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" AS t
            SET
                "Amount" = COALESCE(p."SupplierTotal", 0),
                "WalletAmount" = COALESCE(p."SupplierWalletPrice", 0),
                "UpdatedAt" = NOW()
            FROM "SupplierPurchases" AS p
            WHERE t."SupplierPurchaseId" = p."Id"
              AND t."Source" = 'CostUpdate';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" AS t
            SET
                "Amount" = COALESCE(p."SupplierTotal", 0) + COALESCE(p."SupplierWalletPrice", 0),
                "UpdatedAt" = NOW()
            FROM "SupplierPurchases" AS p
            WHERE t."SupplierPurchaseId" = p."Id"
              AND t."Source" = 'CostUpdate';
            """);
    }
}
