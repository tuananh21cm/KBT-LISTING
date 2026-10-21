using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for staged supplier product snapshots.</summary>
public class SupplierProductInfoConfiguration : IEntityTypeConfiguration<SupplierProductInfo>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SupplierProductInfo> builder)
    {
        builder.ToTable("SupplierProductInfos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.SourceUrlKey).HasMaxLength(200).IsRequired();
        builder.Property(p => p.SourceUrl).HasColumnType("text");
        builder.Property(p => p.AverageRating).HasPrecision(3, 2);
        builder.Property(p => p.SizeGuideJson).HasColumnType("jsonb");
        builder.Property(p => p.SizeChartImageUrl).HasColumnType("text");

        builder.HasIndex(p => p.SourceUrlKey).IsUnique();
        builder.HasIndex(p => p.SupplierSellerInfoId);

        builder.HasOne(p => p.Seller)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SupplierSellerInfoId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
