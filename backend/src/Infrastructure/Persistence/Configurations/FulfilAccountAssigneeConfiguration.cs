using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF config for account ↔ user assignees.</summary>
public class FulfilAccountAssigneeConfiguration : IEntityTypeConfiguration<FulfilAccountAssignee>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FulfilAccountAssignee> builder)
    {
        builder.ToTable("FulfilAccountAssignees");
        builder.HasKey(x => new { x.FulfilAccountId, x.UserId });

        builder.HasOne(x => x.FulfilAccount)
            .WithMany(a => a.Assignees)
            .HasForeignKey(x => x.FulfilAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.UserId);
    }
}
