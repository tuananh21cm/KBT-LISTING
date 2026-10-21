using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for receiving source accounts.</summary>
public class ReceivingSourceAccountConfiguration : IEntityTypeConfiguration<ReceivingSourceAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReceivingSourceAccount> builder)
    {
        builder.ToTable("ReceivingSourceAccounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ExternalAccountId).HasMaxLength(200);
        builder.Property(a => a.Username).HasMaxLength(200);
        builder.Property(a => a.Note).HasMaxLength(500);

        builder.HasIndex(a => a.SourceId);
        builder.HasIndex(a => a.IsActive);
        builder.HasIndex(a => new { a.SourceId, a.Name });

        builder.HasOne(a => a.Source)
            .WithMany(s => s.SourceAccounts)
            .HasForeignKey(a => a.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
