using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for mirrored 4Seller product-discount activities.</summary>
public class ProductDiscountActivityConfiguration : IEntityTypeConfiguration<ProductDiscountActivity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductDiscountActivity> builder)
    {
        builder.ToTable("ProductDiscountActivities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ShopName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.PlatformActivityId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActivityName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DiscountType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PromotionStatus).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProductLevel).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => new { x.OwnerUserId, x.FourSellerActivityId }).IsUnique();
        builder.HasIndex(x => x.ShopId);
        builder.HasIndex(x => x.PromotionStatus);
        builder.HasIndex(x => x.BeginTimeUtc);

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
