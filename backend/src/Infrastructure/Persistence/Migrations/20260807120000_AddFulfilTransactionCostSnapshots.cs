using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807120000_AddFulfilTransactionCostSnapshots")]
public class AddFulfilTransactionCostSnapshots : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "CostAmount",
            table: "FulfilTransactions",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CouponAmount",
            table: "FulfilTransactions",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "WalletAmount",
            table: "FulfilTransactions",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        // Snapshot từ lần mua gắn với dòng CostUpdate — list transactions đọc cột, không GROUP BY purchases.
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" AS t
            SET
                "CostAmount" = COALESCE(p."SupplierSubtotal", 0)
                    + COALESCE(p."SupplierShippingFee", 0)
                    + COALESCE(p."SupplierRetailDeliveryFee", 0)
                    + COALESCE(p."SupplierOnTimeGuarantee", 0)
                    + COALESCE(p."SupplierTax", 0),
                "CouponAmount" = COALESCE(p."SupplierCouponPrice", 0),
                "WalletAmount" = COALESCE(p."SupplierWalletPrice", 0),
                "Amount" = COALESCE(p."SupplierTotal", 0),
                "UpdatedAt" = NOW()
            FROM "SupplierPurchases" AS p
            WHERE t."SupplierPurchaseId" = p."Id"
              AND t."Source" = 'CostUpdate';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CostAmount",
            table: "FulfilTransactions");

        migrationBuilder.DropColumn(
            name: "CouponAmount",
            table: "FulfilTransactions");

        migrationBuilder.DropColumn(
            name: "WalletAmount",
            table: "FulfilTransactions");
    }
}
