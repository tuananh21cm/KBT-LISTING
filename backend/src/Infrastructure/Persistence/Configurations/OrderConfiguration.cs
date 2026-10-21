using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for synced 4Seller orders.
/// </summary>
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.PlatformOrderId).HasMaxLength(100);
        builder.Property(o => o.TikTokPaymentId).HasMaxLength(64);
        builder.Property(o => o.Platform).HasMaxLength(50);
        builder.Property(o => o.OrderStatus).HasMaxLength(50).IsRequired();
        builder.Property(o => o.PlatformOrderStatus).HasMaxLength(100);
        builder.Property(o => o.CancelReason).HasMaxLength(500);
        builder.Property(o => o.ReturnStatus).HasMaxLength(200);
        builder.Property(o => o.Message).HasMaxLength(500);
        builder.Property(o => o.IssueStatus).HasMaxLength(20).IsRequired().HasDefaultValue("None");
        builder.HasIndex(o => o.IssueStatus);
        builder.Property(o => o.Currency).HasMaxLength(10).IsRequired();
        builder.Property(o => o.ShipmentNo).HasMaxLength(100);
        builder.Property(o => o.TrackingNo).HasMaxLength(100);
        builder.Property(o => o.ShippingMethod).HasMaxLength(200);
        builder.Property(o => o.ShippingService).HasMaxLength(200);
        builder.Property(o => o.BuyerName).HasMaxLength(200);
        builder.Property(o => o.BuyerPhone).HasMaxLength(50);
        builder.Property(o => o.BuyerEmail).HasMaxLength(320);
        builder.Property(o => o.Address).HasMaxLength(1000);
        builder.Property(o => o.City).HasMaxLength(100);
        builder.Property(o => o.State).HasMaxLength(100);
        builder.Property(o => o.PostalCode).HasMaxLength(30);
        builder.Property(o => o.CountryCode).HasMaxLength(10);
        builder.Property(o => o.BuyerMemo).HasMaxLength(2000);
        builder.Property(o => o.SellerMemo).HasMaxLength(2000);
        builder.Property(o => o.InternalMemo).HasMaxLength(2000);
        builder.Property(o => o.Amount).HasPrecision(18, 4);
        builder.Property(o => o.TotalPaidAmount).HasPrecision(18, 4);
        builder.Property(o => o.ProductAmountBeforeDiscount).HasPrecision(18, 4);
        builder.Property(o => o.ProductPlatformDiscount).HasPrecision(18, 4);
        builder.Property(o => o.ProductSellerDiscount).HasPrecision(18, 4);
        builder.Property(o => o.ShippingFeeBeforeDiscount).HasPrecision(18, 4);
        builder.Property(o => o.ShippingFeeSellerDiscount).HasPrecision(18, 4);
        builder.Property(o => o.PlatformTotalTax).HasPrecision(18, 4);
        builder.Property(o => o.PlatformOtherFee).HasPrecision(18, 4);
        builder.Property(o => o.SellerNetAmount).HasPrecision(18, 4);
        builder.Property(o => o.Cost).HasPrecision(18, 4);
        builder.Property(o => o.ShippingFee).HasPrecision(18, 4);
        builder.Property(o => o.Tax).HasPrecision(18, 4);
        builder.Property(o => o.Ads).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierRetailPrice).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierSubtotal).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierShippingFee).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierRetailDeliveryFee).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierOnTimeGuarantee).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierCouponPrice).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierMembershipDiscount).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierPointsDiscount).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierWalletPrice).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierTax).HasPrecision(18, 4);
        builder.Property(o => o.ChargedSupplierTotal).HasPrecision(18, 4);
        builder.Property(o => o.ChargedTrueSpend).HasPrecision(18, 4);
        builder.Property(o => o.BuyTrackAmount).HasPrecision(18, 4);
        builder.Property(o => o.ThankCardAmount).HasPrecision(18, 4);
        builder.Property(o => o.PackageWeight).HasPrecision(18, 4);
        builder.Property(o => o.PackageLength).HasPrecision(18, 4);
        builder.Property(o => o.PackageWidth).HasPrecision(18, 4);
        builder.Property(o => o.PackageHeight).HasPrecision(18, 4);
        builder.Property(o => o.PackageWeightUnit).HasMaxLength(10);
        builder.Property(o => o.PackageDimensionUnit).HasMaxLength(10);
        builder.Property(o => o.FulfilTimelineSources).HasColumnType("jsonb");
        builder.Property(o => o.WorkflowTab).HasMaxLength(40);

        builder.HasIndex(o => o.FourSellerOrderId).IsUnique();
        builder.HasIndex(o => o.OwnerId);
        builder.HasIndex(o => o.OrderStatus);
        builder.HasIndex(o => new { o.OwnerId, o.PlatformOrderId })
            .IsUnique()
            .HasFilter("\"PlatformOrderId\" IS NOT NULL AND \"PlatformOrderId\" <> ''");
        builder.HasIndex(o => o.ShopId);
        builder.HasIndex(o => o.TikTokPaymentId)
            .HasFilter("\"TikTokPaymentId\" IS NOT NULL AND \"TikTokPaymentId\" <> ''");
        builder.HasIndex(o => o.LastSyncedAt);
        // Default admin list: filter/sort by paid time (+ status / owner scopes).
        builder.HasIndex(o => new { o.OrderStatus, o.OrderPaidTime });
        builder.HasIndex(o => new { o.OwnerId, o.OrderPaidTime });

        // Denormalized workflow signals: tab filter and badge counts become index scans.
        builder.HasIndex(o => o.WorkflowTab);
        builder.HasIndex(o => new { o.OwnerId, o.WorkflowTab });
        builder.HasIndex(o => new { o.HasUspsSignal, o.UspsProcessedAt });
        builder.HasIndex(o => new { o.HasWarehouseSignal, o.ThankCardProcessedAt });
        builder.HasIndex(o => o.UspsScanBatchId)
            .HasFilter("\"UspsScanBatchId\" IS NOT NULL");

        builder.HasOne(o => o.Owner)
            .WithMany()
            .HasForeignKey(o => o.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Shop)
            .WithMany()
            .HasForeignKey(o => o.ShopId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.FulfilAccount)
            .WithMany()
            .HasForeignKey(o => o.FulfilAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.FulfilCard)
            .WithMany()
            .HasForeignKey(o => o.FulfilCardId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.PaidByUser)
            .WithMany()
            .HasForeignKey(o => o.PaidByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(o => o.FulfilClaimedByUser)
            .WithMany()
            .HasForeignKey(o => o.FulfilClaimedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(o => o.FulfilAccountId);
        builder.HasIndex(o => o.FulfilCardId);
        builder.HasIndex(o => o.PaidByUserId);
        builder.HasIndex(o => o.FulfilPurchasedAt);
        builder.HasIndex(o => o.DeliveredAt);
        builder.HasIndex(o => o.FulfilClaimExpiresAt);
        builder.HasIndex(o => o.FulfilClaimedByUserId);
        // Pending new-order Telegram sweep only scans rows still missing a stamp.
        builder.HasIndex(o => o.TelegramNewOrderNotifiedAt)
            .HasFilter("\"TelegramNewOrderNotifiedAt\" IS NULL");

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
