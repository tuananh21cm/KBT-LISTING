using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for cached 4Seller shipping carriers.
/// </summary>
public class PlatformShippingServiceConfiguration : IEntityTypeConfiguration<PlatformShippingService>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlatformShippingService> builder)
    {
        builder.ToTable("PlatformShippingServices");
        builder.HasKey(s => s.FourSellerId);

        builder.Property(s => s.Carrier).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Platform).HasMaxLength(50);
        builder.Property(s => s.Region).HasMaxLength(20);
        builder.Property(s => s.SyncedAt).IsRequired();

        builder.HasIndex(s => s.Platform);
        builder.HasIndex(s => s.SyncedAt);
    }
}
