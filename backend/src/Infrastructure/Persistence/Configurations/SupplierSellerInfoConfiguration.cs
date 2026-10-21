using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for supplier seller / shop rows.</summary>
public class SupplierSellerInfoConfiguration : IEntityTypeConfiguration<SupplierSellerInfo>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierSellerInfo> builder)
    {
        builder.ToTable("SupplierSellerInfos");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Platform).HasMaxLength(32).IsRequired();
        builder.Property(s => s.StoreCode).HasMaxLength(64).IsRequired();
        builder.Property(s => s.StoreUrl).HasMaxLength(500);
        builder.Property(s => s.StoreLogo).HasColumnType("text");
        builder.Property(s => s.StoreTitle).HasMaxLength(500);
        builder.Property(s => s.SoldByTips).HasMaxLength(500);
        builder.Property(s => s.ShipsFromTips).HasMaxLength(500);
        builder.Property(s => s.ShopRank).HasPrecision(12, 2);
        builder.Property(s => s.ShippingDayPercent).HasPrecision(5, 2);
        builder.Property(s => s.ShippingDayPercentDesc).HasColumnType("text");

        builder.HasIndex(s => new { s.Platform, s.StoreCode }).IsUnique();
    }
}
