namespace Application.DTOs.Shop;

/// <summary>One row from <c>TikTokShopSyncLogs</c>.</summary>
public class TikTokShopSyncLogDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Run id grouping steps from one Sync click.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Owner user id.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Local shop id when known.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>TikTok seller id.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary><c>profile</c> / <c>payout</c> / <c>opportunity</c> / <c>analytics</c>.</summary>
    public string SyncType { get; set; } = string.Empty;

    /// <summary><c>completed</c> / <c>failed</c> / <c>skipped</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Error or skip reason.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>UTC finish time.</summary>
    public DateTime SyncedAt { get; set; }

    /// <summary><c>manual</c> (extension Sync button).</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>Request to record a skipped/failed step without calling the sync endpoint.</summary>
public class RecordTikTokShopSyncLogDto
{
    /// <summary>Run id from extension.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>TikTok seller id when known.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary><c>profile</c> / <c>payout</c> / <c>opportunity</c> / <c>analytics</c>.</summary>
    public string SyncType { get; set; } = string.Empty;

    /// <summary><c>skipped</c> or <c>failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Reason.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>Query for sync log history.</summary>
public class TikTokShopSyncLogQuery
{
    /// <summary>Filter by TikTok seller id.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Filter by sync type.</summary>
    public string? SyncType { get; set; }

    /// <summary>1-based page.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size (max 100).</summary>
    public int PageSize { get; set; } = 50;
}

/// <summary>Paged sync logs.</summary>
public class TikTokShopSyncLogPageDto
{
    /// <summary>Rows.</summary>
    public List<TikTokShopSyncLogDto> Items { get; set; } = [];

    /// <summary>Total matching rows.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int Page { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }
}
