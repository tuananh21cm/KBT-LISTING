using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ShopTaskCompletion"/>.</summary>
public class ShopTaskCompletionConfiguration : IEntityTypeConfiguration<ShopTaskCompletion>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShopTaskCompletion> builder)
    {
        builder.ToTable("ShopTaskCompletions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalShopId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TaskDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.Property(x => x.VisitedUrl).HasMaxLength(1000);
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
        builder.Property(x => x.Source).HasMaxLength(16).IsRequired();

        // Board đọc theo (ngày, owner) và upsert theo (shop, nhiệm vụ, ngày).
        builder.HasIndex(x => new { x.ShopId, x.DefinitionId, x.TaskDate }).IsUnique();
        builder.HasIndex(x => new { x.TaskDate, x.OwnerId });

        builder.HasOne(x => x.Definition)
            .WithMany()
            .HasForeignKey(x => x.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
