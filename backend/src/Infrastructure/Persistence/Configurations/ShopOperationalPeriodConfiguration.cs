using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for shop operational periods.
/// </summary>
public class ShopOperationalPeriodConfiguration : IEntityTypeConfiguration<ShopOperationalPeriod>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShopOperationalPeriod> builder)
    {
        builder.ToTable("ShopOperationalPeriods");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.StartedOn).HasColumnType("date").IsRequired();
        builder.Property(p => p.EndedOn).HasColumnType("date");
        builder.Property(p => p.OpenSource).HasMaxLength(40).IsRequired();
        builder.Property(p => p.CloseSource).HasMaxLength(40);

        builder.HasIndex(p => p.ShopId);
        builder.HasIndex(p => p.OwnerId);
        builder.HasIndex(p => new { p.OwnerId, p.StartedOn, p.EndedOn });
        builder.HasIndex(p => new { p.ShopId, p.EndedOn });

        // At most one open period per shop.
        builder.HasIndex(p => p.ShopId)
            .IsUnique()
            .HasFilter("\"EndedOn\" IS NULL");

        builder.HasOne(p => p.Shop)
            .WithMany()
            .HasForeignKey(p => p.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
