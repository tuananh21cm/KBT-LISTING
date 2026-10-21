using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ListingDelistDaySummary"/>.</summary>
public class ListingDelistDaySummaryConfiguration : IEntityTypeConfiguration<ListingDelistDaySummary>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingDelistDaySummary> builder)
    {
        builder.ToTable("ListingDelistDaySummaries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SnapshotDate).HasColumnType("date").IsRequired();

        // One summary per shop per Vietnam snapshot day — re-sync overwrites.
        builder.HasIndex(x => new { x.ShopId, x.SnapshotDate }).IsUnique();

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
