using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for shops.
/// </summary>
public class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("Shops");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.ExternalShopId).HasMaxLength(100);
        builder.Property(s => s.AvatarUrl).HasMaxLength(2000);
        builder.Property(s => s.FourSellerShopId).HasMaxLength(100);
        builder.Property(s => s.PreviousFourSellerShopId).HasMaxLength(100);
        builder.Property(s => s.Region).HasMaxLength(10).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(10).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.ShopUrl).HasMaxLength(500);
        builder.Property(s => s.Metadata).HasColumnType("jsonb");
        builder.Property(s => s.Proxy).HasMaxLength(100);
        builder.Property(s => s.ProfileName).HasMaxLength(200);
        builder.Property(s => s.Email).HasMaxLength(200);
        builder.Property(s => s.TiktokPassword).HasMaxLength(200);
        builder.Property(s => s.DayLive).HasMaxLength(50);
        builder.Property(s => s.NewPhoneNumber).HasMaxLength(50);
        builder.Property(s => s.OldBankNumber).HasMaxLength(100);
        builder.Property(s => s.NewBankNumber).HasMaxLength(100);
        builder.Property(s => s.PaymentName).HasMaxLength(200);
        builder.Property(s => s.ShopType).HasMaxLength(20);
        builder.Property(s => s.CategoryId).HasMaxLength(100);
        builder.Property(s => s.CategoryName).HasMaxLength(2000);
        builder.Property(s => s.CatalogCategories).HasColumnType("jsonb");
        builder.Property(s => s.StyleCodes).HasColumnType("jsonb");
        builder.Property(s => s.BodySizeCode).HasMaxLength(32);
        builder.Property(s => s.MinRating).HasPrecision(3, 1);
        builder.Property(s => s.PriceMinUsd).HasPrecision(18, 2);
        builder.Property(s => s.PriceMaxUsd).HasPrecision(18, 2);
        builder.Property(s => s.LlcName).HasMaxLength(200);
        builder.Property(s => s.TwoFactorAuth).HasMaxLength(500);
        builder.Property(s => s.SortOrder).IsRequired();
        builder.Property(s => s.ShopCode).HasMaxLength(64);
        builder.Property(s => s.Timezone).HasMaxLength(100);
        builder.Property(s => s.CompanyCertNum).HasMaxLength(100);
        builder.Property(s => s.AccountName).HasMaxLength(200);
        builder.Property(s => s.CompanyAddress).HasColumnType("jsonb");
        builder.Property(s => s.OwnerPerson).HasColumnType("jsonb");
        builder.Property(s => s.LogisticsServices).HasColumnType("jsonb");
        builder.Property(s => s.ProductCategories).HasColumnType("jsonb");
        builder.Property(s => s.SettlementBankAccountNumber).HasMaxLength(100);
        builder.Property(s => s.SettlementPaymentMethod).HasMaxLength(100);
        builder.Property(s => s.SettlementBeneficiaryName).HasMaxLength(200);
        builder.Property(s => s.SettlementAccountType).HasMaxLength(50);
        builder.Property(s => s.OnHoldAmount).HasPrecision(18, 2);
        builder.Property(s => s.OnHoldInTransitAmount).HasPrecision(18, 2);
        builder.Property(s => s.OnHoldAwaitingSettlementAmount).HasPrecision(18, 2);
        builder.Property(s => s.OnHoldReserveAmount).HasPrecision(18, 2);
        builder.Property(s => s.ShopPerformanceScore).HasPrecision(3, 1);
        builder.Property(s => s.ProductSatisfactionScore).HasPrecision(3, 1);
        builder.Property(s => s.FulfillmentScore).HasPrecision(3, 1);
        builder.Property(s => s.CustomerServiceScore).HasPrecision(3, 1);
        builder.Property(s => s.AutoPublishBlockReason).HasMaxLength(64);

        builder.HasIndex(s => new { s.OwnerId, s.ExternalShopId })
            .IsUnique()
            .HasFilter("\"ExternalShopId\" IS NOT NULL AND \"ExternalShopId\" <> ''");
        builder.HasIndex(s => s.OwnerId);
        builder.HasIndex(s => s.PlatformId);
        builder.HasIndex(s => s.SortOrder);
        builder.HasIndex(s => s.ReceivingBankAccountId);

        builder.HasOne(s => s.Owner)
            .WithMany()
            .HasForeignKey(s => s.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Platform)
            .WithMany(p => p.Shops)
            .HasForeignKey(s => s.PlatformId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReceivingBankAccount)
            .WithMany(a => a.Shops)
            .HasForeignKey(s => s.ReceivingBankAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
