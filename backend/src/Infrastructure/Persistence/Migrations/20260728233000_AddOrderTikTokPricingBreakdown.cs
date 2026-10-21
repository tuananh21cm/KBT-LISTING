using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// TikTok / 4Seller orderAmountVo breakdown + seller net (enriched once from order detail).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260728233000_AddOrderTikTokPricingBreakdown")]
public class AddOrderTikTokPricingBreakdown : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "ProductAmountBeforeDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ProductPlatformDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ProductSellerDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ShippingFeeBeforeDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ShippingFeeSellerDiscount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PlatformTotalTax",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PlatformOtherFee",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SellerNetAmount",
            table: "Orders",
            type: "numeric(18,4)",
            precision: 18,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PricingDetailSyncedAt",
            table: "Orders",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ProductAmountBeforeDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "ProductPlatformDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "ProductSellerDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingFeeBeforeDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "ShippingFeeSellerDiscount", table: "Orders");
        migrationBuilder.DropColumn(name: "PlatformTotalTax", table: "Orders");
        migrationBuilder.DropColumn(name: "PlatformOtherFee", table: "Orders");
        migrationBuilder.DropColumn(name: "SellerNetAmount", table: "Orders");
        migrationBuilder.DropColumn(name: "PricingDetailSyncedAt", table: "Orders");
    }
}
