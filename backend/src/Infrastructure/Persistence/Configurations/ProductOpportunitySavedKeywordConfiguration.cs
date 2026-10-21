using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ProductOpportunitySavedKeyword"/>.</summary>
public class ProductOpportunitySavedKeywordConfiguration
    : IEntityTypeConfiguration<ProductOpportunitySavedKeyword>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductOpportunitySavedKeyword> builder)
    {
        builder.ToTable("ProductOpportunitySavedKeywords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Keyword).HasMaxLength(500).IsRequired();
        builder.Property(x => x.KeywordNormalized).HasMaxLength(500).IsRequired();
        builder.Property(x => x.LeadId).HasMaxLength(64);
        builder.Property(x => x.PicUrl).HasMaxLength(2000);

        builder.HasIndex(x => x.OwnerId);
        builder.HasIndex(x => new { x.ShopId, x.KeywordNormalized }).IsUnique();

        builder.HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SavedBy)
            .WithMany()
            .HasForeignKey(x => x.SavedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
