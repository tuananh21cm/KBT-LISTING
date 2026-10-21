using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// TikTok product-discount activity mirrored from 4Seller
/// (<c>POST /api/promotion/tiktok/activity/get-page</c>).
/// </summary>
public class ProductDiscountActivity : BaseEntity
{
    /// <summary>Local primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Seller whose 4Seller cookie was used to pull this row.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Local shop when <see cref="FourSellerShopId"/> maps to an Active shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>4Seller activity id (<c>id</c>).</summary>
    public long FourSellerActivityId { get; set; }

    /// <summary>4Seller shop id (<c>shopId</c>).</summary>
    public long FourSellerShopId { get; set; }

    /// <summary>Shop display name from 4Seller.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>TikTok platform activity id.</summary>
    public string PlatformActivityId { get; set; } = string.Empty;

    /// <summary>Activity display name.</summary>
    public string ActivityName { get; set; } = string.Empty;

    /// <summary>e.g. DIRECT_DISCOUNT, FIXED_PRICE.</summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>e.g. ONGOING, DEACTIVATED, EXPIRED.</summary>
    public string PromotionStatus { get; set; } = string.Empty;

    /// <summary>e.g. PRODUCT.</summary>
    public string ProductLevel { get; set; } = string.Empty;

    /// <summary>Activity start (UTC), from 4Seller epoch ms.</summary>
    public DateTime BeginTimeUtc { get; set; }

    /// <summary>Activity end (UTC), from 4Seller epoch ms.</summary>
    public DateTime EndTimeUtc { get; set; }

    /// <summary>Products in the activity.</summary>
    public int ProductCount { get; set; }

    /// <summary>Failed product count from 4Seller.</summary>
    public int ProductFailCount { get; set; }

    /// <summary>4Seller error message when present.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>UTC when this row was last upserted from sync.</summary>
    public DateTime SyncedAtUtc { get; set; }

    /// <summary>Local shop navigation.</summary>
    public Shop? Shop { get; set; }
}
