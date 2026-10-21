using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for scrape batches.
/// </summary>
public class ScrapeBatchConfiguration : IEntityTypeConfiguration<ScrapeBatch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ScrapeBatch> builder)
    {
        builder.ToTable("ScrapeBatches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(b => b.CreatedById);
        builder.HasIndex(b => b.CreatedAt);

        builder.HasOne(b => b.CreatedBy)
            .WithMany()
            .HasForeignKey(b => b.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
