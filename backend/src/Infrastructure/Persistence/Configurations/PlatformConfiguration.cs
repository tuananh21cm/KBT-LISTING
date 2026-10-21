using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for platforms.
/// </summary>
public class PlatformConfiguration : IEntityTypeConfiguration<Platform>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Platform> builder)
    {
        builder.ToTable("Platforms");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.AvatarUrl).HasMaxLength(1000);

        builder.HasIndex(p => p.Code).IsUnique();
    }
}
