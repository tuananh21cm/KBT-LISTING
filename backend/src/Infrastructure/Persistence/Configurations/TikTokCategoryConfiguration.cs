using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for TikTok category catalog.
/// </summary>
public class TikTokCategoryConfiguration : IEntityTypeConfiguration<TikTokCategory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TikTokCategory> builder)
    {
        builder.ToTable("TikTokCategories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ExternalId).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(500).IsRequired();
        builder.Property(c => c.ParentExternalId).HasMaxLength(64);
        builder.Property(c => c.Site).HasMaxLength(16).IsRequired();
        builder.Property(c => c.Path).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.RawPayload).HasColumnType("jsonb");

        builder.HasIndex(c => new { c.Site, c.ExternalId }).IsUnique();
        builder.HasIndex(c => new { c.Site, c.ParentExternalId });
        builder.HasIndex(c => new { c.Site, c.IsActive });
    }
}
