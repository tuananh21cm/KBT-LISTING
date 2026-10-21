using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for fulfil payment cards.
/// </summary>
public class FulfilCardConfiguration : IEntityTypeConfiguration<FulfilCard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FulfilCard> builder)
    {
        builder.ToTable("FulfilCards");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.CardNumber).HasMaxLength(32).IsRequired();
        builder.Property(c => c.CardBrand).HasMaxLength(20).IsRequired();
        builder.Property(c => c.HolderName).HasMaxLength(200);
        builder.Property(c => c.Cvv).HasMaxLength(10);
        builder.Property(c => c.Currency).HasMaxLength(10).IsRequired();

        builder.HasIndex(c => c.IsActive);
        builder.HasIndex(c => c.SourceId);
        builder.HasIndex(c => c.SourceAccountId);
        builder.HasIndex(c => c.Currency);

        builder.HasOne(c => c.Source)
            .WithMany(s => s.FulfilCards)
            .HasForeignKey(c => c.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.SourceAccount)
            .WithMany(a => a.FulfilCards)
            .HasForeignKey(c => c.SourceAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
