namespace Application.DTOs.Dashboard;

/// <summary>
/// Trend series for charts (VN local buckets by grain).
/// </summary>
public class DashboardTrendsDto
{
    /// <summary>Timezone used for buckets.</summary>
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Bucket grain: day | week | month | year.</summary>
    public string Grain { get; set; } = "day";

    /// <summary>Inclusive range start (yyyy-MM-dd).</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Inclusive range end (yyyy-MM-dd).</summary>
    public string ToDate { get; set; } = string.Empty;

    /// <summary>Breakdown mode when requested: seller | shop (null = aggregate only).</summary>
    public string? GroupBy { get; set; }

    /// <summary>
    /// Aggregate buckets (oldest → newest).
    /// <see cref="DashboardTrendDayDto.Date"/> is the bucket start key.
    /// </summary>
    public List<DashboardTrendDayDto> Days { get; set; } = [];

    /// <summary>
    /// Per-entity series when <see cref="GroupBy"/> is set (top entities + Others).
    /// Empty when aggregate-only.
    /// </summary>
    public List<DashboardTrendSeriesPointDto> Series { get; set; } = [];
}

/// <summary>One time bucket of aggregate trend metrics.</summary>
public class DashboardTrendDayDto
{
    /// <summary>
    /// Bucket key (yyyy-MM-dd): day = that day; week = Monday; month = 1st; year = Jan 1.
    /// </summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>All-status order count (mọi shop / mọi trạng thái).</summary>
    public int Orders { get; set; }

    /// <summary>Orders excluding CANCELLED / REFUND (và legacy canceled khi thiếu platform status).</summary>
    public int OrdersOk { get; set; }

    /// <summary>CANCELLED / CANCELED (và legacy canceled khi thiếu platform status).</summary>
    public int OrdersCanceled { get; set; }

    /// <summary>REFUND / REFUNDED.</summary>
    public int OrdersRefund { get; set; }

    /// <summary>Revenue (Active shop + exclude cancel/refund).</summary>
    public decimal Revenue { get; set; }

    /// <summary>Order total cost sum in bucket.</summary>
    public decimal Cost { get; set; }

    /// <summary>Revenue − cost.</summary>
    public decimal Profit { get; set; }

    /// <summary>Published listings created/updated in bucket.</summary>
    public int Listed { get; set; }
}

/// <summary>One entity × bucket point for seller/shop breakdown charts.</summary>
public class DashboardTrendSeriesPointDto
{
    /// <summary>Bucket key (same as <see cref="DashboardTrendDayDto.Date"/>).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Seller user id or shop id (or <c>__others__</c> / <c>__unassigned__</c>).</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Display name for legend.</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>All-status order count.</summary>
    public int Orders { get; set; }

    /// <summary>Revenue.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Cost.</summary>
    public decimal Cost { get; set; }

    /// <summary>Revenue − cost.</summary>
    public decimal Profit { get; set; }
}
