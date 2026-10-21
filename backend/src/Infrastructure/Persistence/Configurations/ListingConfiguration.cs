using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for synced 4Seller listings.
/// </summary>
public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.ToTable("Listings");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Platform).HasMaxLength(50).IsRequired();
        builder.Property(l => l.PageType).HasMaxLength(20).IsRequired();
        builder.Property(l => l.SiteStatus).HasMaxLength(50);
        builder.Property(l => l.ProductName).HasMaxLength(1000);
        builder.Property(l => l.MainImage).HasColumnType("text");
        builder.Property(l => l.Site).HasMaxLength(20);
        builder.Property(l => l.Currency).HasMaxLength(10).IsRequired();
        builder.Property(l => l.LowPrice).HasPrecision(18, 4);
        builder.Property(l => l.HighPrice).HasPrecision(18, 4);
        builder.Property(l => l.CategoryId).HasMaxLength(100);
        builder.Property(l => l.CategoryName).HasMaxLength(500);
        builder.Property(l => l.WarehouseId).HasMaxLength(100);
        builder.Property(l => l.SourceUrl).HasColumnType("text");
        builder.Property(l => l.AddedFrom).HasMaxLength(20);
        builder.Property(l => l.AverageRating).HasPrecision(3, 2);
        builder.Property(l => l.DescriptionHtml).HasColumnType("text");
        builder.Property(l => l.SearchTermsJson).HasColumnType("jsonb");
        builder.Property(l => l.ProductHighlightsJson).HasColumnType("jsonb");
        builder.Property(l => l.SupplierDescriptionHtml).HasColumnType("text");
        builder.Property(l => l.PackageWeight).HasPrecision(18, 4);
        builder.Property(l => l.PackageLength).HasPrecision(18, 4);
        builder.Property(l => l.PackageWidth).HasPrecision(18, 4);
        builder.Property(l => l.PackageHeight).HasPrecision(18, 4);
        builder.Property(l => l.WeightUnit).HasMaxLength(10);
        builder.Property(l => l.DimensionUnit).HasMaxLength(10);
        builder.Property(l => l.ErpStatus).HasMaxLength(100);
        builder.Property(l => l.PublishStatus).HasMaxLength(100);
        builder.Property(l => l.TikTokProductId).HasMaxLength(100);
        builder.Property(l => l.RawPayload).HasColumnType("jsonb");
        builder.Property(l => l.AppliedPricingJson).HasColumnType("jsonb");
        builder.Property(l => l.SyncError).HasMaxLength(2000);
        builder.Property(l => l.PublishSource).HasMaxLength(16);
        builder.Property(l => l.AutoPublishSkipReason).HasMaxLength(500);
        builder.Property(l => l.AutoPublishCompletedStepsJson).HasColumnType("jsonb");

        // Bảng này từng có 12 index B-tree cho 40 MB heap — 590 MB index. Postgres ghi lại **mọi**
        // index của bảng ở mỗi UPDATE không HOT, kể cả index có cột không đổi, nên năm index một cột
        // dưới đây (OwnerId, PageType, SiteStatus, ShopId, AutoPublishSkipped) chỉ còn là chi phí
        // ghi: cột dẫn đầu của chúng đã nằm trong một composite mà query thật sự dùng, và ba trong
        // số đó là cột phân biệt cực thấp (bool / vài giá trị chuỗi) nên planner chọn seq scan.
        builder.HasIndex(l => new { l.ShopId, l.PageType, l.AutoPublishSkipped, l.CreatedAt });
        builder.HasIndex(l => l.Platform);
        builder.HasIndex(l => l.LastSyncedAt);
        builder.HasIndex(l => new { l.PageType, l.SiteStatus });
        // Admin list: filter PageType then OrderBy PlatformUpdateTime DESC.
        builder.HasIndex(l => new { l.PageType, l.PlatformUpdateTime });
        builder.HasIndex(l => new { l.OwnerId, l.PageType, l.SiteStatus });

        builder.HasOne(l => l.Owner)
            .WithMany()
            .HasForeignKey(l => l.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.Shop)
            .WithMany()
            .HasForeignKey(l => l.ShopId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(l => l.SupplierProductInfo)
            .WithMany(p => p.Listings)
            .HasForeignKey(l => l.SupplierProductInfoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(l => l.SupplierProductInfoId);

        builder.HasMany(l => l.Variations)
            .WithOne(v => v.Listing)
            .HasForeignKey(v => v.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
