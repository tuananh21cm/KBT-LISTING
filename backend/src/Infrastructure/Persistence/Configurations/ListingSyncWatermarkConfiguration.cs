using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for the per-seller listing sync delta watermark.
/// </summary>
public class ListingSyncWatermarkConfiguration : IEntityTypeConfiguration<ListingSyncWatermark>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingSyncWatermark> builder)
    {
        builder.ToTable("ListingSyncWatermarks");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Platform).HasMaxLength(50).IsRequired();
        builder.Property(w => w.Status).HasMaxLength(50).IsRequired();

        builder.HasIndex(w => new { w.OwnerId, w.Platform, w.Status }).IsUnique();
    }
}
