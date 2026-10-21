using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ListingAnalyticsSnapshot"/>.</summary>
public class ListingAnalyticsSnapshotConfiguration : IEntityTypeConfiguration<ListingAnalyticsSnapshot>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingAnalyticsSnapshot> builder)
    {
        builder.ToTable("ListingAnalyticsSnapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TikTokProductId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SnapshotDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.RangeStart).HasColumnType("date").IsRequired();
        builder.Property(x => x.RangeEnd).HasColumnType("date").IsRequired();
        builder.Property(x => x.Preset).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProductName).HasMaxLength(500);
        builder.Property(x => x.MainImage).HasMaxLength(1000);
        builder.Property(x => x.Currency).HasMaxLength(8).IsRequired();

        builder.Property(x => x.Gmv).HasPrecision(18, 2);
        builder.Property(x => x.Ctr).HasPrecision(12, 6);
        builder.Property(x => x.GmvRatio).HasPrecision(12, 6);
        builder.Property(x => x.OrdersRatio).HasPrecision(12, 6);
        builder.Property(x => x.ImpressionsRatio).HasPrecision(12, 6);
        builder.Property(x => x.ClicksRatio).HasPrecision(12, 6);
        builder.Property(x => x.CtrRatio).HasPrecision(12, 6);
        builder.Property(x => x.AddToCartUnitsRatio).HasPrecision(12, 6);
        builder.Property(x => x.AddToCartBuyersRatio).HasPrecision(12, 6);

        builder.Property(x => x.RawStatsJson).HasColumnType("jsonb");

        // One row per shop product per Vietnam day — a repeat sync overwrites it.
        builder.HasIndex(x => new { x.ShopId, x.TikTokProductId, x.SnapshotDate }).IsUnique();
        builder.HasIndex(x => new { x.OwnerId, x.SnapshotDate });
        builder.HasIndex(x => new { x.ListingId, x.SnapshotDate });

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        // Keep the snapshot when a listing is deleted; the next sync re-links by product id.
        builder.HasOne(x => x.Listing)
            .WithMany()
            .HasForeignKey(x => x.ListingId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
