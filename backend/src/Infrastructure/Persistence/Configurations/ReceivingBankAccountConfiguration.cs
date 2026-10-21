using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for receiving bank accounts.
/// </summary>
public class ReceivingBankAccountConfiguration : IEntityTypeConfiguration<ReceivingBankAccount>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReceivingBankAccount> builder)
    {
        builder.ToTable("ReceivingBankAccounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Currency).HasMaxLength(10).IsRequired();
        builder.Property(a => a.BankName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.BankLocation).HasMaxLength(100);
        builder.Property(a => a.AccountHolderName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.AccountType).HasMaxLength(50);
        builder.Property(a => a.SwiftBic).HasMaxLength(20);
        builder.Property(a => a.RoutingNumber).HasMaxLength(50);
        builder.Property(a => a.AccountNumber).HasMaxLength(100).IsRequired();
        builder.Property(a => a.BankAddress).HasMaxLength(500);

        builder.HasIndex(a => a.IsActive);
        builder.HasIndex(a => a.AccountNumber);
        builder.HasIndex(a => a.SourceId);
        builder.HasIndex(a => a.SourceAccountId);

        builder.HasOne(a => a.Source)
            .WithMany(s => s.ReceivingBankAccounts)
            .HasForeignKey(a => a.SourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.SourceAccount)
            .WithMany(s => s.ReceivingBankAccounts)
            .HasForeignKey(a => a.SourceAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
