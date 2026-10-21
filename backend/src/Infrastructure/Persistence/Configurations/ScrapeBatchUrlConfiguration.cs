using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for scrape batch URLs.
/// </summary>
public class ScrapeBatchUrlConfiguration : IEntityTypeConfiguration<ScrapeBatchUrl>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ScrapeBatchUrl> builder)
    {
        builder.ToTable("ScrapeBatchUrls");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.SourceUrl).HasMaxLength(2000).IsRequired();
        builder.Property(u => u.SourceUrlKey).HasMaxLength(500).IsRequired();

        builder.HasIndex(u => u.BatchId);
        builder.HasIndex(u => u.SourceUrlKey);
        builder.HasIndex(u => u.MatchedMoveDraftId);

        builder.HasOne(u => u.Batch)
            .WithMany(b => b.Urls)
            .HasForeignKey(u => u.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
