using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for permissions.
/// </summary>
public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Module).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Application).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.HasIndex(p => p.Code).IsUnique();
    }
}
