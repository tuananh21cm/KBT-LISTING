using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ShopTaskDefinition"/>.</summary>
public class ShopTaskDefinitionConfiguration : IEntityTypeConfiguration<ShopTaskDefinition>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShopTaskDefinition> builder)
    {
        builder.ToTable("ShopTaskDefinitions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.UrlMatchPattern).HasMaxLength(1000);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.ViewDurationSeconds).HasDefaultValue(0);

        builder.HasIndex(x => new { x.IsActive, x.SortOrder });
    }
}
