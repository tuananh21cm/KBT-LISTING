using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for auto publish run logs.</summary>
public class ListingAutoPublishRunConfiguration : IEntityTypeConfiguration<ListingAutoPublishRun>
{
    public void Configure(EntityTypeBuilder<ListingAutoPublishRun> builder)
    {
        builder.ToTable("ListingAutoPublishRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasMaxLength(32).IsRequired();
        builder.Property(r => r.FailedStepCode).HasMaxLength(64);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);
        builder.Property(r => r.StepsJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.ListingId, r.StartedAt });
        builder.HasIndex(r => new { r.ShopId, r.StartedAt });
        // Scheduler eligibility: EXISTS … Status=running AND ShopId AND StartedAt >= cutoff
        builder.HasIndex(r => new { r.Status, r.ShopId, r.StartedAt });

        builder.HasOne(r => r.Listing)
            .WithMany()
            .HasForeignKey(r => r.ListingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Shop)
            .WithMany()
            .HasForeignKey(r => r.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
