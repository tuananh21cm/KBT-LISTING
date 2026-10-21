namespace Application.DTOs.Dashboard;

/// <summary>
/// Optional scope filters for admin dashboard aggregates.
/// </summary>
public class DashboardQuery
{
    /// <summary>Filter by seller owner.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Filter by local shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Filter by platform code.</summary>
    public string? Platform { get; set; }

    /// <summary>
    /// Period for period KPIs + seller ranking: <c>day</c> | <c>week</c> | <c>month</c> (default day).
    /// Calendar windows in Asia/Ho_Chi_Minh: today / Mon–today / 1st–today.
    /// Ignored when <see cref="FromDate"/> and <see cref="ToDate"/> are both set.
    /// </summary>
    public string Period { get; set; } = "day";

    /// <summary>
    /// Inclusive range start (yyyy-MM-dd, Asia/Ho_Chi_Minh). When set with <see cref="ToDate"/>,
    /// overrides <see cref="Period"/>.
    /// </summary>
    public string? FromDate { get; set; }

    /// <summary>
    /// Inclusive range end (yyyy-MM-dd, Asia/Ho_Chi_Minh).
    /// </summary>
    public string? ToDate { get; set; }

    /// <summary>
    /// Optional comma-separated summary sections to compute.
    /// Empty/null = full snapshot (backward compatible).
    /// Allowed: <c>kpis</c>, <c>cardSpend</c> (deprecated no-op when <c>kpis</c> present),
    /// <c>orderStatus</c>, <c>listings</c>, <c>shops</c>, <c>sellers</c>,
    /// <c>shopsFinance</c>, <c>topProducts</c>.
    /// </summary>
    public string? Include { get; set; }

    /// <summary>
    /// Slim metric set for stats pages.
    /// <c>orders</c> → sellers / shopsFinance / trends skip revenue·cost·Items joins
    /// (sort by orders). <c>revenue</c> → trends keep orders+revenue but skip cost·Items
    /// (Revenue stats page). Null/empty = full finance metrics (backward compatible).
    /// Does not affect KPI section when <c>include</c> contains <c>kpis</c>.
    /// </summary>
    public string? Metrics { get; set; }
}

/// <summary>
/// Query for dashboard trend series.
/// </summary>
public class DashboardTrendsQuery : DashboardQuery
{
    /// <summary>
    /// Number of local VN days (default 7, clamp 1–31).
    /// Ignored when <see cref="DashboardQuery.Period"/> is <c>week</c> or <c>month</c>
    /// (series follows the calendar window), or when <see cref="DashboardQuery.FromDate"/> / <see cref="DashboardQuery.ToDate"/> are set.
    /// </summary>
    public int Days { get; set; } = 7;

    /// <summary>
    /// Bucket size: <c>day</c> | <c>week</c> | <c>month</c> | <c>year</c> (default <c>day</c>).
    /// Week buckets start Monday (ISO).
    /// </summary>
    public string Grain { get; set; } = "day";

    /// <summary>
    /// Optional breakdown: <c>seller</c> | <c>shop</c>.
    /// When set, response includes per-entity series (top N + Others) alongside aggregate <c>days</c>.
    /// </summary>
    public string? GroupBy { get; set; }

    /// <summary>
    /// Heatmap: day heatmap uses <c>seller</c> | <c>shop</c>;
    /// hourly heatmap uses <c>total</c> | <c>seller</c>.
    /// When set, only that dimension is computed (faster). Null/empty = both (legacy).
    /// </summary>
    public string? Dimension { get; set; }

    /// <summary>
    /// Hourly heatmap only: hour-bucket timezone <c>us</c> (America/Los_Angeles, default)
    /// or <c>vn</c> (Asia/Ho_Chi_Minh). Period date window always uses VN calendar.
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// When <c>false</c>, skip published-listing scans and return <c>listed=0</c> per bucket.
    /// Default <c>true</c> (null = true) for Dashboard / orders stats that show listed.
    /// </summary>
    public bool? IncludeListed { get; set; }
}
