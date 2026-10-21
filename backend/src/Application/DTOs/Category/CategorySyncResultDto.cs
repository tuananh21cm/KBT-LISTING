namespace Application.DTOs.Category;

/// <summary>
/// Result of a full category tree sync from 4Seller.
/// </summary>
public class CategorySyncResultDto
{
    /// <summary>Site that was synced.</summary>
    public string Site { get; set; } = string.Empty;

    /// <summary>4Seller shop ID used for the API calls.</summary>
    public long FourSellerShopId { get; set; }

    /// <summary>Total nodes upserted (created + updated).</summary>
    public int SyncedCount { get; set; }

    /// <summary>Newly created nodes.</summary>
    public int CreatedCount { get; set; }

    /// <summary>Existing nodes updated.</summary>
    public int UpdatedCount { get; set; }

    /// <summary>Nodes marked inactive (missing from this sync).</summary>
    public int DeactivatedCount { get; set; }

    /// <summary>UTC timestamp when sync finished.</summary>
    public DateTime SyncedAt { get; set; }
}
