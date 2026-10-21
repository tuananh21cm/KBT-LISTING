using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="OrderIssueTag"/>.</summary>
public class OrderIssueTagConfiguration : IEntityTypeConfiguration<OrderIssueTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderIssueTag> builder)
    {
        builder.ToTable("OrderIssueTags");
        builder.HasKey(x => new { x.OrderId, x.IssueTagId });

        builder.HasOne(x => x.Order)
            .WithMany(o => o.IssueTags)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.IssueTag)
            .WithMany(t => t.OrderAssignments)
            .HasForeignKey(x => x.IssueTagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedBy)
            .WithMany()
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.IssueTagId);
    }
}
