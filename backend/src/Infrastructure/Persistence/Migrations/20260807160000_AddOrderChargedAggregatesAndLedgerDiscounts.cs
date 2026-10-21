using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <inheritdoc />
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260807160000_AddOrderChargedAggregatesAndLedgerDiscounts")]
public class AddOrderChargedAggregatesAndLedgerDiscounts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierRetailPrice",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierSubtotal",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierShippingFee",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierRetailDeliveryFee",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierOnTimeGuarantee",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierCouponPrice",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierMembershipDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierPointsDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierWalletPrice",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierTax",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedSupplierTotal",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "ChargedTrueSpend",
            table: "Orders",
            type: "numeric(18,4)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "MembershipAmount",
            table: "FulfilTransactions",
            type: "numeric(18,4)",
            nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "PointsAmount",
            table: "FulfilTransactions",
            type: "numeric(18,4)",
            nullable: true);

        // Backfill Orders.Charged* từ SupplierPurchases IsCharged.
        migrationBuilder.Sql(
            """
            UPDATE "Orders" AS o
            SET
                "ChargedSupplierRetailPrice" = a."Retail",
                "ChargedSupplierSubtotal" = a."Subtotal",
                "ChargedSupplierShippingFee" = a."Ship",
                "ChargedSupplierRetailDeliveryFee" = a."Rdf",
                "ChargedSupplierOnTimeGuarantee" = a."OnTime",
                "ChargedSupplierCouponPrice" = a."Coupon",
                "ChargedSupplierMembershipDiscount" = a."Club",
                "ChargedSupplierPointsDiscount" = a."Points",
                "ChargedSupplierWalletPrice" = a."Wallet",
                "ChargedSupplierTax" = a."Tax",
                "ChargedSupplierTotal" = a."Total",
                "ChargedTrueSpend" = COALESCE(a."Total", 0) + COALESCE(a."Wallet", 0)
            FROM (
                SELECT
                    p."OrderId",
                    SUM(p."SupplierRetailPrice") FILTER (WHERE p."SupplierRetailPrice" IS NOT NULL) AS "Retail",
                    SUM(p."SupplierSubtotal") FILTER (WHERE p."SupplierSubtotal" IS NOT NULL) AS "Subtotal",
                    SUM(p."SupplierShippingFee") FILTER (WHERE p."SupplierShippingFee" IS NOT NULL) AS "Ship",
                    SUM(p."SupplierRetailDeliveryFee") FILTER (WHERE p."SupplierRetailDeliveryFee" IS NOT NULL) AS "Rdf",
                    SUM(p."SupplierOnTimeGuarantee") FILTER (WHERE p."SupplierOnTimeGuarantee" IS NOT NULL) AS "OnTime",
                    SUM(p."SupplierCouponPrice") FILTER (WHERE p."SupplierCouponPrice" IS NOT NULL) AS "Coupon",
                    SUM(p."SupplierMembershipDiscount") FILTER (WHERE p."SupplierMembershipDiscount" IS NOT NULL) AS "Club",
                    SUM(p."SupplierPointsDiscount") FILTER (WHERE p."SupplierPointsDiscount" IS NOT NULL) AS "Points",
                    SUM(p."SupplierWalletPrice") FILTER (WHERE p."SupplierWalletPrice" IS NOT NULL) AS "Wallet",
                    SUM(p."SupplierTax") FILTER (WHERE p."SupplierTax" IS NOT NULL) AS "Tax",
                    SUM(p."SupplierTotal") FILTER (WHERE p."SupplierTotal" IS NOT NULL) AS "Total"
                FROM "SupplierPurchases" AS p
                WHERE p."OrderId" IS NOT NULL AND p."IsCharged" = TRUE
                GROUP BY p."OrderId"
            ) AS a
            WHERE o."Id" = a."OrderId";
            """);

        // TrueSpend null khi cả Total và Wallet null (đơn chưa có tiền).
        migrationBuilder.Sql(
            """
            UPDATE "Orders"
            SET "ChargedTrueSpend" = NULL
            WHERE "ChargedSupplierTotal" IS NULL AND "ChargedSupplierWalletPrice" IS NULL;
            """);

        // Backfill ledger Membership/Points từ purchase.
        migrationBuilder.Sql(
            """
            UPDATE "FulfilTransactions" AS t
            SET
                "MembershipAmount" = COALESCE(p."SupplierMembershipDiscount", 0),
                "PointsAmount" = COALESCE(p."SupplierPointsDiscount", 0),
                "UpdatedAt" = NOW()
            FROM "SupplierPurchases" AS p
            WHERE t."SupplierPurchaseId" = p."Id"
              AND t."Source" = 'CostUpdate';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ChargedSupplierRetailPrice", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierSubtotal", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierShippingFee", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierRetailDeliveryFee", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierOnTimeGuarantee", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierCouponPrice", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierMembershipDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierPointsDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierWalletPrice", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierTax", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedSupplierTotal", table: "Orders");
        migrationBuilder.DropColumn(name: "ChargedTrueSpend", table: "Orders");
        migrationBuilder.DropColumn(name: "MembershipAmount", table: "FulfilTransactions");
        migrationBuilder.DropColumn(name: "PointsAmount", table: "FulfilTransactions");
    }
}
