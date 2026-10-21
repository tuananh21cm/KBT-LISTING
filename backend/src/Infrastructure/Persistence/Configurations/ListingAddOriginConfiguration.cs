using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for post-html listing origin staging.</summary>
public class ListingAddOriginConfiguration : IEntityTypeConfiguration<ListingAddOrigin>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ListingAddOrigin> builder)
    {
        builder.ToTable("ListingAddOrigins");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.SourceUrlKey).HasMaxLength(200).IsRequired();
        builder.Property(o => o.AddedFrom).HasMaxLength(20).IsRequired();

        builder.HasIndex(o => o.OwnerId);
        builder.HasIndex(o => new { o.OwnerId, o.SourceUrlKey, o.FourSellerShopId }).IsUnique();

        builder.HasOne(o => o.Owner)
            .WithMany()
            .HasForeignKey(o => o.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
