using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>EF configuration for <see cref="ProductOpportunityShopLink"/>.</summary>
public class ProductOpportunityShopLinkConfiguration
    : IEntityTypeConfiguration<ProductOpportunityShopLink>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProductOpportunityShopLink> builder)
    {
        builder.ToTable("ProductOpportunityShopLinks");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => new { x.ShopId, x.ProductOpportunityId }).IsUnique();
        builder.HasIndex(x => x.ProductOpportunityId);

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ProductOpportunity)
            .WithMany(x => x.ShopLinks)
            .HasForeignKey(x => x.ProductOpportunityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
