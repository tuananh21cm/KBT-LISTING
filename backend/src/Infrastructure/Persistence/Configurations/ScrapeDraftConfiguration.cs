using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for scrape draft ownership.
/// </summary>
public class ScrapeDraftConfiguration : IEntityTypeConfiguration<ScrapeDraft>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ScrapeDraft> builder)
    {
        builder.ToTable("ScrapeDrafts");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.SourceUrl).HasMaxLength(2000);
        builder.Property(d => d.SourceUrlKey).HasMaxLength(500).IsRequired();
        builder.Property(d => d.ProductName).HasMaxLength(500);
        builder.Property(d => d.ImageUrl).HasMaxLength(2000);
        builder.Property(d => d.Currency).HasMaxLength(10);
        builder.Property(d => d.Creator).HasMaxLength(200);
        builder.Property(d => d.FourSellerCreateTime).HasMaxLength(50);
        builder.Property(d => d.LowPrice).HasPrecision(18, 4);
        builder.Property(d => d.HighPrice).HasPrecision(18, 4);

        builder.HasIndex(d => new { d.CreatedById, d.FourSellerMoveDraftId }).IsUnique();
        builder.HasIndex(d => d.CreatedById);
        builder.HasIndex(d => d.SourceUrlKey);
        builder.HasIndex(d => d.BatchId);
        builder.HasIndex(d => new { d.CreatedById, d.LastSeenAt });

        builder.HasOne(d => d.CreatedBy)
            .WithMany()
            .HasForeignKey(d => d.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Batch)
            .WithMany(b => b.Drafts)
            .HasForeignKey(d => d.BatchId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
