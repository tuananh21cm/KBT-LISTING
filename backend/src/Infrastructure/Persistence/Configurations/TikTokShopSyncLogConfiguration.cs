using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="TikTokShopSyncLog"/>.</summary>
public class TikTokShopSyncLogConfiguration : IEntityTypeConfiguration<TikTokShopSyncLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TikTokShopSyncLog> builder)
    {
        builder.ToTable("TikTokShopSyncLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RunId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalShopId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SyncType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);

        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => new { x.OwnerId, x.ShopId, x.SyncType, x.SyncedAt })
            .IsDescending(false, false, false, true);
        builder.HasIndex(x => new { x.ExternalShopId, x.SyncedAt })
            .IsDescending(false, true);
    }
}
