using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for PrintPackShip USPS scan batches.</summary>
public class UspsScanBatchConfiguration : IEntityTypeConfiguration<UspsScanBatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UspsScanBatch> builder)
    {
        builder.ToTable("UspsScanBatches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.ExternalBatchId).IsRequired();
        builder.Property(b => b.Status).HasMaxLength(30).IsRequired();
        builder.Property(b => b.Notes).HasMaxLength(500);
        builder.Property(b => b.ErrorMessage).HasMaxLength(2000);
        builder.Property(b => b.PricePerLabel).HasPrecision(18, 4);
        builder.Property(b => b.ScanFee).HasPrecision(18, 4);
        builder.Property(b => b.BalancePrevious).HasPrecision(18, 4);
        builder.Property(b => b.BalanceCurrent).HasPrecision(18, 4);
        builder.Property(b => b.RawCreateResponse).HasColumnType("text");
        builder.Property(b => b.RawSendResponse).HasColumnType("text");

        builder.HasIndex(b => b.ExternalBatchId).IsUnique();
        builder.HasIndex(b => b.Status);
        builder.HasIndex(b => b.CreatedAt);

        builder.HasOne(b => b.CreatedByUser)
            .WithMany()
            .HasForeignKey(b => b.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(b => b.Items)
            .WithOne(i => i.Batch!)
            .HasForeignKey(i => i.BatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>EF configuration for USPS scan batch items.</summary>
public class UspsScanBatchItemConfiguration : IEntityTypeConfiguration<UspsScanBatchItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UspsScanBatchItem> builder)
    {
        builder.ToTable("UspsScanBatchItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TrackingNo).HasMaxLength(100).IsRequired();

        builder.HasIndex(i => i.TrackingNo);
        builder.HasIndex(i => new { i.BatchId, i.OrderId }).IsUnique();

        builder.HasOne(i => i.Order)
            .WithMany()
            .HasForeignKey(i => i.OrderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
