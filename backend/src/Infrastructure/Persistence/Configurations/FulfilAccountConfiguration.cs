using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for fulfil purchase accounts.
/// </summary>
public class FulfilAccountConfiguration : IEntityTypeConfiguration<FulfilAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FulfilAccount> builder)
    {
        builder.ToTable("FulfilAccounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Username).HasMaxLength(200);
        builder.Property(a => a.Password).HasMaxLength(200);
        builder.Property(a => a.Email).HasMaxLength(320);
        builder.Property(a => a.PassMail).HasMaxLength(200);
        builder.Property(a => a.TwoFactorAuth).HasMaxLength(500);
        builder.Property(a => a.SheinProxy).HasMaxLength(200);
        builder.Property(a => a.SheinOrigin).HasMaxLength(100);
        builder.Property(a => a.SheinSessionLastError).HasMaxLength(500);

        builder.HasIndex(a => a.Name);
        builder.HasIndex(a => a.IsActive);
        builder.HasIndex(a => a.Username)
            .IsUnique()
            .HasFilter("\"Username\" IS NOT NULL AND \"Username\" <> ''");
        builder.HasIndex(a => a.Email)
            .IsUnique()
            .HasFilter("\"Email\" IS NOT NULL AND \"Email\" <> ''");

        // Circular FK with cards: ActiveCard is optional; cards own the required side via join.
        builder.HasOne(a => a.ActiveCard)
            .WithMany()
            .HasForeignKey(a => a.ActiveCardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
