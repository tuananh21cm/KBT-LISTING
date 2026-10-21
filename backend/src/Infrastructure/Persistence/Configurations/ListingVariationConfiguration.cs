using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for synced listing variations.
/// </summary>
public class ListingVariationConfiguration : IEntityTypeConfiguration<ListingVariation>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingVariation> builder)
    {
        builder.ToTable("ListingVariations");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.SellerSku).HasMaxLength(200);
        builder.Property(v => v.Title).HasMaxLength(500);
        builder.Property(v => v.MainImage).HasColumnType("text");
        builder.Property(v => v.SalePrice).HasPrecision(18, 4);
        builder.Property(v => v.InitialPrice).HasPrecision(18, 4);
        builder.Property(v => v.GlobalPrice).HasPrecision(18, 4);
        builder.Property(v => v.VariantAttributes).HasColumnType("jsonb");

        builder.HasIndex(v => v.ListingId);
        builder.HasIndex(v => new { v.ListingId, v.FourSellerSkuId });
    }
}
