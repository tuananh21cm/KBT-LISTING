using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for system settings.
/// </summary>
public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("Settings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Group).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Label).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Value).HasMaxLength(2000).IsRequired();
        builder.Property(s => s.Type).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);

        builder.HasIndex(s => s.Key).IsUnique();
    }
}
