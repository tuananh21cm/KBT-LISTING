using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for variation spec templates.
/// </summary>
public class VariationSpecTemplateConfiguration : IEntityTypeConfiguration<VariationSpecTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<VariationSpecTemplate> builder)
    {
        builder.ToTable("VariationSpecTemplates");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.SpecType).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Value).HasMaxLength(100).IsRequired();

        builder.HasIndex(p => new { p.SpecType, p.Value }).IsUnique();
        builder.HasIndex(p => new { p.SpecType, p.SortOrder });
    }
}
