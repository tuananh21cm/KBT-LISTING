using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for the company income/expense ledger.
/// </summary>
public class CompanyLedgerEntryConfiguration : IEntityTypeConfiguration<CompanyLedgerEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CompanyLedgerEntry> builder)
    {
        builder.ToTable("CompanyLedgerEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Amount).HasPrecision(18, 2);
        builder.Property(e => e.Currency).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Direction).HasMaxLength(20).IsRequired();
        builder.Property(e => e.CategoryCode).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Title).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(2000);
        builder.Property(e => e.Source).HasMaxLength(30).IsRequired();

        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => e.CategoryCode);
        builder.HasIndex(e => e.Direction);
        builder.HasIndex(e => e.OwnerUserId);
        builder.HasIndex(e => e.ShopId);
        builder.HasIndex(e => e.Source);
        builder.HasIndex(e => e.CreatedByUserId);

        builder.HasIndex(e => e.ShopId)
            .IsUnique()
            .HasDatabaseName("IX_CompanyLedgerEntries_ShopId_ShopAcquisition")
            .HasFilter($"\"ShopId\" IS NOT NULL AND \"Source\" = '{CompanyLedgerEntry.SourceShopAcquisition}'");

        builder.HasOne(e => e.Shop)
            .WithMany()
            .HasForeignKey(e => e.ShopId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
