using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for product sources.
/// </summary>
public class ProductSourceConfiguration : IEntityTypeConfiguration<ProductSource>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductSource> builder)
    {
        builder.ToTable("ProductSources");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.LogoUrl).HasMaxLength(1000);
        builder.Property(p => p.Domains).HasMaxLength(2000);
        builder.Property(p => p.Description).HasMaxLength(500);

        builder.HasIndex(p => p.Code).IsUnique();
    }
}
