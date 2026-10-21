using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ShopOnlineSession"/>.</summary>
public class ShopOnlineSessionConfiguration : IEntityTypeConfiguration<ShopOnlineSession>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShopOnlineSession> builder)
    {
        builder.ToTable("ShopOnlineSessions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalShopId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ClientSessionId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CloseReason).HasMaxLength(40);

        builder.HasIndex(x => new { x.ShopId, x.ClientSessionId }).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.StartedAt });
        builder.HasIndex(x => new { x.ExternalShopId, x.StartedAt });

        // At most one open session per shop.
        builder.HasIndex(x => x.ShopId)
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL");

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
