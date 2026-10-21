using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="IssueTag"/>.</summary>
public class IssueTagConfiguration : IEntityTypeConfiguration<IssueTag>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IssueTag> builder)
    {
        builder.ToTable("IssueTags");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Label).HasMaxLength(50).IsRequired();
        builder.Property(t => t.IsSystem).IsRequired();

        builder.HasIndex(t => t.Label).IsUnique();

        builder.HasOne(t => t.CreatedBy)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
