using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="SupplierPurchase"/>.</summary>
public class SupplierPurchaseConfiguration : IEntityTypeConfiguration<SupplierPurchase>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierPurchase> builder)
    {
        builder.ToTable("SupplierPurchases");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupplierOrderNo).HasMaxLength(64);
        builder.Property(x => x.Source).HasMaxLength(20).IsRequired();
        builder.Property(x => x.MatchState).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SkipReason).HasMaxLength(40);
        builder.Property(x => x.SkipEvidenceJson).HasColumnType("text");
        builder.Property(x => x.RelationBillno).HasMaxLength(64);
        builder.Property(x => x.ConfluenceBatchNo).HasMaxLength(80);
        builder.Property(x => x.SupplierOrderUrl).HasMaxLength(512);
        builder.Property(x => x.TrackingNo).HasMaxLength(100);
        builder.Property(x => x.WarehouseTrackingNo).HasMaxLength(100);
        builder.Property(x => x.Carrier).HasMaxLength(100);
        builder.Property(x => x.TrackingKind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ProductIdsJson).HasColumnType("text");
        builder.Property(x => x.SupplierStatus).HasMaxLength(32);
        builder.Property(x => x.SupplierStatusRaw).HasMaxLength(128);
        builder.Property(x => x.ReceiverName).HasMaxLength(200);
        builder.Property(x => x.ReceiverPhone).HasMaxLength(50);
        builder.Property(x => x.ReceiverPostalCode).HasMaxLength(30);
        builder.Property(x => x.PhoneKey).HasMaxLength(50);
        builder.Property(x => x.ZipKey).HasMaxLength(30);
        builder.Property(x => x.NameKey).HasMaxLength(200);
        builder.Property(x => x.LastRunId).HasMaxLength(64);
        builder.Property(x => x.AutoMarkShipTrackingNo).HasMaxLength(100);
        builder.Property(x => x.AutoMarkShipStatus).HasMaxLength(20);
        builder.Property(x => x.AutoMarkShipError).HasMaxLength(1000);
        builder.Property(x => x.AutoMarkShipRequestId).HasMaxLength(100);
        builder.Property(x => x.RawPayload).HasColumnType("text");

        // Hàng cũ chưa từng được quan sát trạng thái thanh toán ⇒ false = "chưa biết", giữ đúng
        // hành vi tính giá mua trước đây cho tới lượt Sync Fulfil kế tiếp.
        builder.Property(x => x.SupplierUnpaid).HasDefaultValue(false);
        builder.Property(x => x.IsMembershipOrVirtual).HasDefaultValue(false);
        builder.Property(x => x.IsCharged).HasDefaultValue(false);

        builder.Property(x => x.SupplierRetailPrice).HasPrecision(18, 4);
        builder.Property(x => x.SupplierSubtotal).HasPrecision(18, 4);
        builder.Property(x => x.SupplierShippingFee).HasPrecision(18, 4);
        builder.Property(x => x.SupplierRetailDeliveryFee).HasPrecision(18, 4);
        builder.Property(x => x.SupplierOnTimeGuarantee).HasPrecision(18, 4);
        builder.Property(x => x.SupplierCouponPrice).HasPrecision(18, 4);
        builder.Property(x => x.SupplierMembershipDiscount).HasPrecision(18, 4);
        builder.Property(x => x.SupplierPointsDiscount).HasPrecision(18, 4);
        builder.Property(x => x.SupplierWalletPrice).HasPrecision(18, 4);
        builder.Property(x => x.SupplierTax).HasPrecision(18, 4);
        builder.Property(x => x.SupplierTotal).HasPrecision(18, 4);

        // Unique có điều kiện: dòng nhập tay không có mã NCC nào, và Postgres coi mọi NULL là khác
        // nhau nên không lọc thì mọi dòng manual vẫn qua được — nhưng lọc tường minh thì ý định đọc
        // ra được từ schema.
        builder.HasIndex(x => x.SupplierOrderNo)
            .IsUnique()
            .HasFilter("\"SupplierOrderNo\" IS NOT NULL");

        builder.HasIndex(x => x.OrderId);
        // Không có btree cho TrackingNo: mọi query trên cột này là `Contains` (LIKE '%…%', btree
        // không dùng được — GIN pg_trgm phục vụ) hoặc `== null` trong bộ lọc hàng đợi mark-ship.
        // Một index chỉ tốn chi phí ghi ở mỗi lượt sync thì không giữ.
        builder.HasIndex(x => x.RelationBillno);
        builder.HasIndex(x => x.ConfluenceBatchNo);
        builder.HasIndex(x => x.SupplierStatus);
        builder.HasIndex(x => x.AutoMarkShipStatus);
        builder.HasIndex(x => new { x.AutoMarkShipStatus, x.TrackingKind });
        builder.HasIndex(x => x.FulfilAccountId);
        builder.HasIndex(x => x.FulfilCardId);
        builder.HasIndex(x => x.MatchState);
        builder.HasIndex(x => new { x.PhoneKey, x.ZipKey });

        // SetNull, không Cascade: gỡ một đơn sàn không được xoá bằng chứng đã mua — dòng mua quay về
        // hàng đợi chưa gắn thay vì biến mất cùng đơn.
        builder.HasOne(x => x.Order)
            .WithMany(o => o.SupplierPurchases)
            .HasForeignKey(x => x.OrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.FulfilAccount)
            .WithMany()
            .HasForeignKey(x => x.FulfilAccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.FulfilCard)
            .WithMany()
            .HasForeignKey(x => x.FulfilCardId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PaidByUser)
            .WithMany()
            .HasForeignKey(x => x.PaidByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
