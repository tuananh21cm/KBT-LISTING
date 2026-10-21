using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for order line items.
/// </summary>
public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.SellerSku).HasMaxLength(200);
        builder.Property(i => i.Title).HasMaxLength(1000);
        builder.Property(i => i.ImgUrl).HasColumnType("text");
        builder.Property(i => i.ListingUrl).HasColumnType("text");
        builder.Property(i => i.SourceUrl).HasColumnType("text");
        builder.Property(i => i.VariantAttr).HasMaxLength(500);
        builder.Property(i => i.Currency).HasMaxLength(10);
        builder.Property(i => i.ItemPrice).HasPrecision(18, 4);

        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => new { i.OrderId, i.FourSellerOrderItemId }).IsUnique();
    }
}
