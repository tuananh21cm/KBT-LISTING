using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="AgeRange"/>.</summary>
public class AgeRangeConfiguration : IEntityTypeConfiguration<AgeRange>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AgeRange> builder)
    {
        builder.ToTable("AgeRanges");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PainPoint).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Desire).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ProductGroups).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.SortOrder);
    }
}
