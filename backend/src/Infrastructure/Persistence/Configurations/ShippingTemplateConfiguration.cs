using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for per-user shipping templates.
/// </summary>
public class ShippingTemplateConfiguration : IEntityTypeConfiguration<ShippingTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShippingTemplate> builder)
    {
        builder.ToTable("ShippingTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.PackageWeight).HasPrecision(10, 3);
        builder.Property(t => t.PackageLength).HasPrecision(10, 2);
        builder.Property(t => t.PackageWidth).HasPrecision(10, 2);
        builder.Property(t => t.PackageHeight).HasPrecision(10, 2);
        builder.Property(t => t.WeightUnit).HasMaxLength(10).IsRequired();
        builder.Property(t => t.DimensionUnit).HasMaxLength(10).IsRequired();
        builder.Property(t => t.IsDefault).HasDefaultValue(false);

        builder.HasIndex(t => new { t.CreatedById, t.Name }).IsUnique();
        builder.HasIndex(t => t.CreatedById);

        builder.HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
