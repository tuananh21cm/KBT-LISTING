using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for receiving-bank transaction ledger.
/// </summary>
public class ReceivingBankTransactionConfiguration : IEntityTypeConfiguration<ReceivingBankTransaction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReceivingBankTransaction> builder)
    {
        builder.ToTable("ReceivingBankTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount).HasPrecision(18, 4);
        builder.Property(t => t.Currency).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(2000);
        builder.Property(t => t.ExternalTransactionId).HasMaxLength(200);
        builder.Property(t => t.PayoutDetailsJson).HasColumnType("jsonb");
        builder.Property(t => t.Source).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Direction).HasMaxLength(20).IsRequired();
        builder.Property(t => t.ConfirmationStatus).HasMaxLength(20).IsRequired();

        builder.HasIndex(t => t.ReceivingBankAccountId);
        builder.HasIndex(t => t.ShopId);
        builder.HasIndex(t => t.OccurredAt);
        builder.HasIndex(t => t.Source);
        builder.HasIndex(t => t.Direction);
        builder.HasIndex(t => t.ConfirmationStatus);

        // Unique when set — prevent duplicate platform payout ids.
        builder.HasIndex(t => t.ExternalTransactionId)
            .IsUnique()
            .HasDatabaseName("IX_ReceivingBankTransactions_ExternalTransactionId")
            .HasFilter("\"ExternalTransactionId\" IS NOT NULL");

        // At most one auto SettlementPaid transaction per order.
        builder.HasIndex(t => t.OrderId)
            .IsUnique()
            .HasDatabaseName("IX_ReceivingBankTransactions_OrderId_SettlementPaid")
            .HasFilter($"\"OrderId\" IS NOT NULL AND \"Source\" = '{ReceivingBankTransaction.SourceSettlementPaid}'");

        builder.HasIndex(t => t.OrderId)
            .HasDatabaseName("IX_ReceivingBankTransactions_OrderId");

        builder.HasOne(t => t.ReceivingBankAccount)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.ReceivingBankAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Shop)
            .WithMany()
            .HasForeignKey(t => t.ShopId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.Order)
            .WithMany()
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
