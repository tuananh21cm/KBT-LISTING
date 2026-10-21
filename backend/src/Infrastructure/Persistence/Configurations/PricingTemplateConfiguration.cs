using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for per-user pricing templates.
/// </summary>
public class PricingTemplateConfiguration : IEntityTypeConfiguration<PricingTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PricingTemplate> builder)
    {
        builder.ToTable("PricingTemplates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.SupplierShipping).HasPrecision(10, 2);
        builder.Property(t => t.Tax).HasPrecision(10, 2);
        builder.Property(t => t.DiscountPercent).HasPrecision(5, 2);
        builder.Property(t => t.FastSalePercent).HasPrecision(5, 2);
        builder.Property(t => t.AdsFee).HasPrecision(10, 2);
        builder.Property(t => t.TikTokShipping).HasPrecision(10, 2);
        builder.Property(t => t.PlatformFeePercent).HasPrecision(5, 2);
        builder.Property(t => t.TargetMode).HasMaxLength(20).IsRequired();
        builder.Property(t => t.MarginPercent).HasPrecision(10, 2);
        builder.Property(t => t.ProfitAmount).HasPrecision(10, 2);

        builder.HasIndex(t => new { t.CreatedById, t.Name }).IsUnique();
        builder.HasIndex(t => t.CreatedById);

        builder.HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
