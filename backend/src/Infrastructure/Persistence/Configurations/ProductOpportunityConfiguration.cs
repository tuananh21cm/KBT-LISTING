using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ProductOpportunity"/>.</summary>
public class ProductOpportunityConfiguration : IEntityTypeConfiguration<ProductOpportunity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductOpportunity> builder)
    {
        builder.ToTable("ProductOpportunities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.LeadId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.LeadName).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PicUrl).HasMaxLength(2000);
        builder.Property(x => x.PicUrls).HasColumnType("jsonb");
        builder.Property(x => x.OnlineProductsRaw).HasMaxLength(32);
        builder.Property(x => x.SearchVolumeRingRatio).HasMaxLength(32);
        builder.Property(x => x.OnlineProductsRingRatio).HasMaxLength(32);
        builder.Property(x => x.OrderRaw).HasMaxLength(32);
        builder.Property(x => x.OrderRingRatio).HasMaxLength(32);
        builder.Property(x => x.Gmv).HasPrecision(18, 2);
        builder.Property(x => x.GmvRaw).HasMaxLength(32);
        builder.Property(x => x.GmvRingRatio).HasMaxLength(32);
        builder.Property(x => x.Categories).HasColumnType("jsonb");
        builder.Property(x => x.Level1CateId).HasMaxLength(32);
        builder.Property(x => x.Level1CateName).HasMaxLength(200);
        builder.Property(x => x.Level2CateId).HasMaxLength(32);
        builder.Property(x => x.Level2CateName).HasMaxLength(200);
        builder.Property(x => x.Level3CateId).HasMaxLength(32);
        builder.Property(x => x.Level3CateName).HasMaxLength(200);
        builder.Property(x => x.TagCodes).HasColumnType("jsonb");
        builder.Property(x => x.LeadTagTexts).HasColumnType("jsonb");
        builder.Property(x => x.LeadSourceTag).HasMaxLength(64);
        builder.Property(x => x.LeadClusterSourceTag).HasMaxLength(64);

        builder.HasIndex(x => x.LeadId).IsUnique();
        builder.HasIndex(x => x.SearchVolume);
    }
}
