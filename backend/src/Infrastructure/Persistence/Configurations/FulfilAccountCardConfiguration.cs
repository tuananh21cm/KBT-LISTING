using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for fulfil account ↔ card links.
/// </summary>
public class FulfilAccountCardConfiguration : IEntityTypeConfiguration<FulfilAccountCard>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FulfilAccountCard> builder)
    {
        builder.ToTable("FulfilAccountCards");
        builder.HasKey(x => new { x.FulfilAccountId, x.FulfilCardId });

        builder.HasOne(x => x.FulfilAccount)
            .WithMany(a => a.CardLinks)
            .HasForeignKey(x => x.FulfilAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FulfilCard)
            .WithMany(c => c.AccountLinks)
            .HasForeignKey(x => x.FulfilCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.FulfilCardId);
    }
}
