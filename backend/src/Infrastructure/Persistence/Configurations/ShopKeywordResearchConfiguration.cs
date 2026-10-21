using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ShopKeywordResearch"/>.</summary>
public class ShopKeywordResearchConfiguration : IEntityTypeConfiguration<ShopKeywordResearch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ShopKeywordResearch> builder)
    {
        builder.ToTable("ShopKeywordResearches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => x.ShopId).IsUnique();

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
