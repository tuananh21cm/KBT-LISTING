namespace Application.DTOs.Promotion;

/// <summary>List query for product-discount activities (local DB).</summary>
public class ProductDiscountListQuery
{
    /// <summary>1-based page.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Filter by local shop id.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Admin: filter by seller owner.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Filter promotion status (ONGOING, …).</summary>
    public string? PromotionStatus { get; set; }

    /// <summary>Filter discount type.</summary>
    public string? DiscountType { get; set; }

    /// <summary>Search activity name (contains).</summary>
    public string? Search { get; set; }
}

/// <summary>Paged local product-discount activities.</summary>
public class ProductDiscountListPageDto
{
    /// <summary>Rows.</summary>
    public List<ProductDiscountListItemDto> Items { get; set; } = [];

    /// <summary>Total matching filters.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int Page { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }
}

/// <summary>One product-discount activity for list UI.</summary>
public class ProductDiscountListItemDto
{
    /// <summary>Local id.</summary>
    public Guid Id { get; set; }

    /// <summary>Owner seller id.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Owner display name.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Local shop id when mapped.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Shop name.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>4Seller activity id.</summary>
    public long FourSellerActivityId { get; set; }

    /// <summary>Platform activity id.</summary>
    public string PlatformActivityId { get; set; } = string.Empty;

    /// <summary>Activity name.</summary>
    public string ActivityName { get; set; } = string.Empty;

    /// <summary>Discount type.</summary>
    public string DiscountType { get; set; } = string.Empty;

    /// <summary>Promotion status.</summary>
    public string PromotionStatus { get; set; } = string.Empty;

    /// <summary>Product level.</summary>
    public string ProductLevel { get; set; } = string.Empty;

    /// <summary>Begin UTC.</summary>
    public DateTime BeginTimeUtc { get; set; }

    /// <summary>End UTC.</summary>
    public DateTime EndTimeUtc { get; set; }

    /// <summary>Product count.</summary>
    public int ProductCount { get; set; }

    /// <summary>Failed product count.</summary>
    public int ProductFailCount { get; set; }

    /// <summary>Error message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Last sync UTC.</summary>
    public DateTime SyncedAtUtc { get; set; }
}

/// <summary>Manual sync request body.</summary>
public class ProductDiscountSyncRequest
{
    /// <summary>Admin: limit to one seller; seller portal always forces JWT.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// 4Seller discountType filter. Empty = product-discount page; <c>FLASHSALE</c> = flash deals.
    /// </summary>
    public string? DiscountType { get; set; }
}

/// <summary>Aggregate sync result.</summary>
public class ProductDiscountSyncResultDto
{
    /// <summary>Sellers synced successfully.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>Rows inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated.</summary>
    public int Updated { get; set; }

    /// <summary>Records skipped (inactive / unmapped shop).</summary>
    public int Skipped { get; set; }

    /// <summary>Per-seller failures.</summary>
    public List<ProductDiscountSyncSellerFailureDto> FailedSellers { get; set; } = [];
}

/// <summary>One seller sync failure.</summary>
public class ProductDiscountSyncSellerFailureDto
{
    /// <summary>Owner user id.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Display name.</summary>
    public string? OwnerName { get; set; }

    /// <summary>Error message.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Per-seller progress for job polling.</summary>
public class ProductDiscountSyncSellerProgressDto
{
    /// <summary>Owner user id.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Display name.</summary>
    public string? OwnerName { get; set; }

    /// <summary>pending / running / succeeded / failed.</summary>
    public string Status { get; set; } = "pending";

    /// <summary>Inserted for this seller.</summary>
    public int Inserted { get; set; }

    /// <summary>Updated for this seller.</summary>
    public int Updated { get; set; }

    /// <summary>Skipped for this seller.</summary>
    public int Skipped { get; set; }

    /// <summary>Elapsed ms once left pending.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Failure message.</summary>
    public string? Message { get; set; }
}

/// <summary>Snapshot of a manual product-discount sync job.</summary>
public class ProductDiscountSyncJobStatusDto
{
    /// <summary>Job id for polling.</summary>
    public Guid JobId { get; set; }

    /// <summary>running / completed / failed.</summary>
    public string State { get; set; } = "running";

    /// <summary>When queued.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>When finished.</summary>
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>Sellers in scope.</summary>
    public int TotalSellers { get; set; }

    /// <summary>Sellers that reached a terminal status.</summary>
    public int CompletedSellers { get; set; }

    /// <summary>Sellers synced successfully.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>Rows inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated.</summary>
    public int Updated { get; set; }

    /// <summary>Rows skipped.</summary>
    public int Skipped { get; set; }

    /// <summary>Summary when finished.</summary>
    public string? Message { get; set; }

    /// <summary>Error when state is failed.</summary>
    public string? Error { get; set; }

    /// <summary>Per-seller progress.</summary>
    public List<ProductDiscountSyncSellerProgressDto> Sellers { get; set; } = [];
}
