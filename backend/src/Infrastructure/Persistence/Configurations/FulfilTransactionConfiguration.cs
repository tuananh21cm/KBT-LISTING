using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for fulfil transaction ledger.
/// </summary>
public class FulfilTransactionConfiguration : IEntityTypeConfiguration<FulfilTransaction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FulfilTransaction> builder)
    {
        builder.ToTable("FulfilTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Amount).HasPrecision(18, 4);
        builder.Property(t => t.CouponAmount).HasPrecision(18, 4);
        builder.Property(t => t.WalletAmount).HasPrecision(18, 4);
        builder.Property(t => t.MembershipAmount).HasPrecision(18, 4);
        builder.Property(t => t.PointsAmount).HasPrecision(18, 4);
        builder.Property(t => t.Currency).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Note).HasMaxLength(2000);
        builder.Property(t => t.Source).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Direction).HasMaxLength(20).IsRequired();

        builder.HasIndex(t => t.FulfilAccountId);
        builder.HasIndex(t => t.FulfilCardId);
        builder.HasIndex(t => t.OccurredAt);
        builder.HasIndex(t => t.Source);
        builder.HasIndex(t => t.Direction);

        // Named overloads: HasIndex(property) alone is keyed by the property set, so a second
        // call on the same column would rename the first index instead of adding one.

        // One auto CostUpdate row per supplier purchase — a Joint Shipment order is charged
        // once per billno, so the ledger must have that same granularity to match a statement.
        builder.HasIndex(t => t.SupplierPurchaseId, "IX_FulfilTransactions_PurchaseId_CostUpdate")
            .IsUnique()
            .HasFilter(
                $"\"SupplierPurchaseId\" IS NOT NULL AND \"Source\" = '{FulfilTransaction.SourceCostUpdate}'");

        // Hàng cũ sinh trước khi nút "Cập nhật phí" tự tạo dòng mua: một dòng cho mỗi đơn.
        builder.HasIndex(t => t.OrderId, "IX_FulfilTransactions_OrderId_CostUpdate")
            .IsUnique()
            .HasFilter(
                $"\"OrderId\" IS NOT NULL AND \"SupplierPurchaseId\" IS NULL AND \"Source\" = '{FulfilTransaction.SourceCostUpdate}'");

        builder.HasIndex(t => t.OrderId, "IX_FulfilTransactions_OrderId");

        builder.HasIndex(t => t.SupplierPurchaseId, "IX_FulfilTransactions_SupplierPurchaseId");

        builder.HasOne(t => t.FulfilAccount)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.FulfilAccountId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.FulfilCard)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.FulfilCardId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Order)
            .WithMany()
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.SetNull);

        // SetNull (not Cascade) so deleting an order keeps its card spend in the ledger,
        // matching the Order FK above.
        builder.HasOne(t => t.SupplierPurchase)
            .WithMany()
            .HasForeignKey(t => t.SupplierPurchaseId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.PaidByUser)
            .WithMany()
            .HasForeignKey(t => t.PaidByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
