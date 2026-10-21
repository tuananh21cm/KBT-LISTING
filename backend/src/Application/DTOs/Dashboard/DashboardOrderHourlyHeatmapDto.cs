namespace Application.DTOs.Dashboard;

/// <summary>
/// Order-count heatmap: hour-of-day (0–23, America/Los_Angeles) aggregated across the selected VN date range.
/// Rows = all sellers (totals) and/or per-seller.
/// </summary>
public class DashboardOrderHourlyHeatmapDto
{
    /// <summary>Timezone used for hour buckets (US Pacific).</summary>
    public string Timezone { get; set; } = "America/Los_Angeles";

    /// <summary>Resolved period: day | week | month.</summary>
    public string Period { get; set; } = "day";

    /// <summary>Range start (inclusive), yyyy-MM-dd (VN business calendar).</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Range end (inclusive), yyyy-MM-dd (VN business calendar).</summary>
    public string ToDate { get; set; } = string.Empty;

    /// <summary>
    /// All-sellers aggregate: 24 cells (hour 0–23). Empty when <c>dimension=seller</c>.
    /// </summary>
    public List<DashboardHourlyHeatmapCellDto> Totals { get; set; } = [];

    /// <summary>
    /// Seller × hour cells (top sellers + Others; zeros filled). Empty when <c>dimension=total</c>.
    /// </summary>
    public List<DashboardHourlyHeatmapCellDto> Sellers { get; set; } = [];
}

/// <summary>One cell: orders at a given hour, aggregated over the selected range.</summary>
public class DashboardHourlyHeatmapCellDto
{
    /// <summary>Seller user id, <c>__others__</c>, or <c>__all__</c> for totals.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Display label (seller name, Others, or Tổng).</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Local hour of day 0–23.</summary>
    public int Hour { get; set; }

    /// <summary>Non-canceled order count (sum across all days in range).</summary>
    public int Orders { get; set; }
}
