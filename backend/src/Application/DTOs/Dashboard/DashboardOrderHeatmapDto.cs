namespace Application.DTOs.Dashboard;

/// <summary>
/// Order-count heatmap for seller and shop dimensions (VN local days).
/// </summary>
public class DashboardOrderHeatmapDto
{
    /// <summary>Timezone used for day buckets.</summary>
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Resolved period: day | week | month.</summary>
    public string Period { get; set; } = "day";

    /// <summary>Range start (inclusive), yyyy-MM-dd.</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Range end (inclusive), yyyy-MM-dd.</summary>
    public string ToDate { get; set; } = string.Empty;

    /// <summary>Date × seller cells (top sellers + Others; zeros filled).</summary>
    public List<DashboardHeatmapCellDto> Sellers { get; set; } = [];

    /// <summary>Date × shop cells (top shops + Others; zeros filled).</summary>
    public List<DashboardHeatmapCellDto> Shops { get; set; } = [];
}

/// <summary>One heatmap cell: orders on a local day for one entity.</summary>
public class DashboardHeatmapCellDto
{
    /// <summary>Local VN date yyyy-MM-dd.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Seller user id or shop id (or synthetic Others / Unassigned).</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Display label for the Y axis.</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Non-canceled order count.</summary>
    public int Orders { get; set; }

    /// <summary>
    /// Optional group key (shop heatmap: seller user id). Null for seller heatmap rows.
    /// </summary>
    public string? GroupId { get; set; }

    /// <summary>Optional group label (shop heatmap: seller display name).</summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// Shop cells only: TikTok <c>publish_limit.published_num</c> (synced). Null for seller cells / Others.
    /// </summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>
    /// Shop cells only: TikTok <c>publish_limit.total_limit</c> (synced). Null for seller cells / Others.
    /// </summary>
    public int? PublishLimitTotal { get; set; }
}
