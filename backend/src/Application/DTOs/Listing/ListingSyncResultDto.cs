namespace Application.DTOs.Listing;

/// <summary>Result of listing sync.</summary>
public class ListingSyncResultDto
{
    /// <summary>Sellers synced successfully.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>Rows inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated.</summary>
    public int Updated { get; set; }

    /// <summary>
    /// In-flight / stale pending rows re-checked by id and updated during reconcile.
    /// </summary>
    public int ReconciledUpdated { get; set; }

    /// <summary>
    /// Local mirrors removed after Manual reconcile proved they no longer exist on 4Seller.
    /// </summary>
    public int ReconciledRemoved { get; set; }

    /// <summary>
    /// Local Publishing/UnderReview/pending rows still present after reconcile (true Reviewing or backlog).
    /// </summary>
    public int StillInFlight { get; set; }

    /// <summary>Per-seller failures.</summary>
    public List<ListingSyncSellerFailureDto> FailedSellers { get; set; } = [];

    /// <summary>
    /// True when this single-seller sync could not acquire a per-seller lease
    /// (another pull for the same account is in progress). Post-html draft scheduler retries.
    /// </summary>
    public bool SkippedBusy { get; set; }
}

/// <summary>Per-seller counts returned by <c>SyncOwnerAsync</c>.</summary>
public class ListingOwnerSyncResultDto
{
    /// <summary>Rows inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated.</summary>
    public int Updated { get; set; }

    /// <summary>Reconcile-by-id updates.</summary>
    public int ReconciledUpdated { get; set; }

    /// <summary>Local rows deleted as gone from 4Seller.</summary>
    public int ReconciledRemoved { get; set; }

    /// <summary>In-flight/pending remaining after this seller pass.</summary>
    public int StillInFlight { get; set; }
}

/// <summary>One seller sync failure.</summary>
public class ListingSyncSellerFailureDto
{
    /// <summary>Owner user id.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Display name.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Error message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Local delete result.</summary>
public class ListingDeleteResultDto
{
    /// <summary>Deleted count.</summary>
    public int DeletedCount { get; set; }
}

/// <summary>Bulk deactivate (Hủy list) result.</summary>
public class ListingDeactivateResultDto
{
    /// <summary>Listings deactivated on 4Seller and marked inactive locally.</summary>
    public int DeactivatedCount { get; set; }

    /// <summary>
    /// Ids skipped (not found, missing 4Seller id, or not Active).
    /// </summary>
    public int SkippedCount { get; set; }
}

/// <summary>Push save-draft / publish result.</summary>
public class ListingPushResultDto
{
    /// <summary>Whether push succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Message.</summary>
    public string? Message { get; set; }

    /// <summary>Updated local listing id.</summary>
    public Guid ListingId { get; set; }

    /// <summary>Page type after push.</summary>
    public string? PageType { get; set; }

    /// <summary>Site status after push.</summary>
    public string? SiteStatus { get; set; }

    /// <summary>TikTok product id when known.</summary>
    public string? TikTokProductId { get; set; }
}
