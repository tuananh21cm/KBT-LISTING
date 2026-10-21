using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="SheinSyncRunLog"/>.</summary>
public class SheinSyncRunLogConfiguration : IEntityTypeConfiguration<SheinSyncRunLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SheinSyncRunLog> builder)
    {
        builder.ToTable("SheinSyncRunLogs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RunId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Phase).HasMaxLength(16).IsRequired();
        builder.Property(x => x.UpdateMode).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FulfilAccountName).HasMaxLength(200);

        // Danh sách billno kèm chi tiết — dài tuỳ lô, không cắt bằng maxlength.
        builder.Property(x => x.SkipCountsJson).HasColumnType("text");
        builder.Property(x => x.SkipOutcomesJson).HasColumnType("text");
        builder.Property(x => x.NeedsDetailReasonsJson).HasColumnType("text");

        builder.HasIndex(x => x.RunId);
        builder.HasIndex(x => x.CreatedAt).IsDescending();
    }
}
