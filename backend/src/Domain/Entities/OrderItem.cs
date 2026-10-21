using Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

/// <summary>
/// Line item for a synced 4Seller order.
/// </summary>
public class OrderItem : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Parent order id.</summary>
    public Guid OrderId { get; set; }

    /// <summary>4Seller order item id.</summary>
    public long FourSellerOrderItemId { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Product title.</summary>
    public string? Title { get; set; }

    /// <summary>Product image URL.</summary>
    public string? ImgUrl { get; set; }

    /// <summary>Listing URL on the platform.</summary>
    public string? ListingUrl { get; set; }

    /// <summary>Source / supplier URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Source catalog image URL, populated for read models only.</summary>
    [NotMapped]
    public string? SourceImgUrl { get; set; }

    /// <summary>Listing average rating (read overlay via OwnerId + SellerSku).</summary>
    [NotMapped]
    public decimal? AverageRating { get; set; }

    /// <summary>Listing review count (read overlay via OwnerId + SellerSku).</summary>
    [NotMapped]
    public int? ReviewCount { get; set; }

    /// <summary>Supplier shop title (overlay from listing → SupplierSellerInfos).</summary>
    [NotMapped]
    public string? StoreTitle { get; set; }

    /// <summary>Supplier shop URL (overlay).</summary>
    [NotMapped]
    public string? StoreUrl { get; set; }

    /// <summary>Shein shopRank (overlay).</summary>
    [NotMapped]
    public decimal? ShopRank { get; set; }

    /// <summary>Shein storeAttention / followers (overlay).</summary>
    [NotMapped]
    public int? StoreAttention { get; set; }

    /// <summary>Supplier shipping SLA days (overlay).</summary>
    [NotMapped]
    public int? ShippingDays { get; set; }

    /// <summary>Supplier % delivered within SLA (overlay).</summary>
    [NotMapped]
    public decimal? ShippingDayPercent { get; set; }

    /// <summary>Local US stock flag (overlay from SupplierProductInfos).</summary>
    [NotMapped]
    public bool? IsLocalProduct { get; set; }

    /// <summary>Quick-ship flag (overlay from SupplierProductInfos).</summary>
    [NotMapped]
    public bool? IsSupportQuickShip { get; set; }

    /// <summary>Variant attributes (e.g. color, size).</summary>
    public string? VariantAttr { get; set; }

    /// <summary>Quantity ordered.</summary>
    public int Quantity { get; set; }

    /// <summary>Unit price.</summary>
    public decimal ItemPrice { get; set; }

    /// <summary>Currency code.</summary>
    public string? Currency { get; set; }

    /// <summary>Parent order.</summary>
    public Order Order { get; set; } = null!;
}
