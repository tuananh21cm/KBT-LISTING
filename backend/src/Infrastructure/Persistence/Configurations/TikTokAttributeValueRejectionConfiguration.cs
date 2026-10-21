using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for learned TikTok attribute value rejections.
/// </summary>
public class TikTokAttributeValueRejectionConfiguration
    : IEntityTypeConfiguration<TikTokAttributeValueRejection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TikTokAttributeValueRejection> builder)
    {
        builder.ToTable("TikTokAttributeValueRejections");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.CategoryId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.AttributeId).HasMaxLength(64).IsRequired();
        builder.Property(r => r.AttributeName).HasMaxLength(200);
        builder.Property(r => r.ValueId).HasMaxLength(64);
        builder.Property(r => r.ValueName).HasMaxLength(300).IsRequired();
        builder.Property(r => r.ValueNameNorm).HasMaxLength(300).IsRequired();
        builder.Property(r => r.Source).HasMaxLength(30).IsRequired();
        builder.Property(r => r.SampleError).HasMaxLength(1000);

        // CategoryId is never null (AllCategories sentinel instead), so the unique index actually
        // dedupes: a Postgres unique index treats NULLs as distinct and would let duplicates through.
        builder
            .HasIndex(r => new { r.CategoryId, r.AttributeId, r.ValueNameNorm })
            .IsUnique();

        builder.HasIndex(r => r.AttributeId);
    }
}
