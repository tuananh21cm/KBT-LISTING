using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF config for card ↔ user assignees.</summary>
public class FulfilCardAssigneeConfiguration : IEntityTypeConfiguration<FulfilCardAssignee>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FulfilCardAssignee> builder)
    {
        builder.ToTable("FulfilCardAssignees");
        builder.HasKey(x => new { x.FulfilCardId, x.UserId });

        builder.HasOne(x => x.FulfilCard)
            .WithMany(c => c.Assignees)
            .HasForeignKey(x => x.FulfilCardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId);
    }
}
