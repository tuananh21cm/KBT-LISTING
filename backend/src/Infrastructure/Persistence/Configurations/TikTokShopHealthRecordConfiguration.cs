using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="TikTokShopHealthRecord"/>.</summary>
public class TikTokShopHealthRecordConfiguration : IEntityTypeConfiguration<TikTokShopHealthRecord>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TikTokShopHealthRecord> builder)
    {
        builder.ToTable("TikTokShopHealthRecords");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RecordId).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RecordType).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CategoryType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ActionTitlesJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.ShopId, x.RecordId }).IsUnique();
        builder.HasIndex(x => new { x.ShopId, x.RecordType, x.RecordStatus });
        builder.HasIndex(x => new { x.ShopId, x.CreateTime })
            .IsDescending(false, true);
        builder.HasIndex(x => new { x.ShopId, x.HandledAt })
            .IsDescending(false, true);
    }
}
