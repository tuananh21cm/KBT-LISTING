namespace Application.DTOs.Shop;

/// <summary>
/// Counts from cascading shop ownership reassignment (orders/listings/denorm rows).
/// </summary>
public class ShopOwnershipTransferResultDto
{
    /// <summary>Shop that was reassigned.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Previous seller cookie owner.</summary>
    public Guid FromOwnerId { get; set; }

    /// <summary>New seller cookie owner.</summary>
    public Guid ToOwnerId { get; set; }

    /// <summary>Orders rematched onto <see cref="ShopId"/> before OwnerId update.</summary>
    public int OrdersRematched { get; set; }

    /// <summary>Listings rematched onto <see cref="ShopId"/> before OwnerId update.</summary>
    public int ListingsRematched { get; set; }

    /// <summary>Orders whose <c>OwnerId</c> moved.</summary>
    public int OrdersTransferred { get; set; }

    /// <summary>Listings whose <c>OwnerId</c> moved.</summary>
    public int ListingsTransferred { get; set; }

    /// <summary><see cref="Domain.Entities.ListingAnalyticsSnapshot"/> rows updated.</summary>
    public int AnalyticsSnapshotsTransferred { get; set; }

    /// <summary><see cref="Domain.Entities.ProductDiscountActivity"/> rows updated.</summary>
    public int DiscountActivitiesTransferred { get; set; }

    /// <summary><see cref="Domain.Entities.ProductOpportunitySavedKeyword"/> rows updated.</summary>
    public int SavedKeywordsTransferred { get; set; }

    /// <summary><see cref="Domain.Entities.ListingAddOrigin"/> rows updated.</summary>
    public int ListingAddOriginsTransferred { get; set; }

    /// <summary>Duplicate PlatformOrderId pairs merged into one row.</summary>
    public int OrdersMerged { get; set; }

    /// <summary>
    /// Duplicate listings on the same shop with the same TikTok product id collapsed
    /// (keep richest mirror, delete thin shells).
    /// </summary>
    public int ListingsCollapsed { get; set; }

    /// <summary>
    /// Shops processed in a bulk reassign (1 for single-shop endpoints).
    /// </summary>
    public int ShopsProcessed { get; set; }

    /// <summary>True when any counter above is non-zero.</summary>
    public bool HasChanges =>
        OrdersRematched > 0
        || ListingsRematched > 0
        || OrdersTransferred > 0
        || ListingsTransferred > 0
        || AnalyticsSnapshotsTransferred > 0
        || DiscountActivitiesTransferred > 0
        || SavedKeywordsTransferred > 0
        || ListingAddOriginsTransferred > 0
        || OrdersMerged > 0
        || ListingsCollapsed > 0;
}
