namespace Application.DTOs.Dashboard;

/// <summary>
/// Query for seller performance stats (all active sellers in a VN date window).
/// Reuses the same period / fromDate / toDate resolution as dashboard summary.
/// </summary>
public class SellerPerformanceQuery : DashboardQuery
{
}

/// <summary>
/// Seller performance snapshot for the selected period.
/// </summary>
public class SellerPerformanceDto
{
    /// <summary>Timezone used for period buckets.</summary>
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Resolved period: day | week | month (or day when custom range).</summary>
    public string Period { get; set; } = "day";

    /// <summary>Period start (inclusive), local VN yyyy-MM-dd.</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Period end (inclusive), local VN yyyy-MM-dd.</summary>
    public string ToDate { get; set; } = string.Empty;

    /// <summary>Weighted totals across all sellers in scope.</summary>
    public SellerPerformanceTotalsDto Totals { get; set; } = new();

    /// <summary>
    /// Daily growth series for all sellers in scope, or one seller when <c>ownerUserId</c> is set.
    /// </summary>
    public List<SellerPerformanceDayDto> Days { get; set; } = [];

    /// <summary>
    /// Every active Seller-role user (including zero-order rows), sorted by profit DESC then revenue DESC.
    /// </summary>
    public List<SellerPerformanceRowDto> Sellers { get; set; } = [];
}

/// <summary>One VN-calendar point in a selected seller's growth series.</summary>
public class SellerPerformanceDayDto
{
    /// <summary>VN calendar date (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Non-canceled orders on this day (including null ShopId).</summary>
    public int Orders { get; set; }

    /// <summary>Non-canceled orders with ShopId on this day.</summary>
    public int EligibleShopOrders { get; set; }

    /// <summary>Operational shops whose interval covers this day.</summary>
    public int OperationalShopCount { get; set; }

    /// <summary>Daily orders; explicit field for chart/API semantics.</summary>
    public decimal OrdersPerDay { get; set; }

    /// <summary>EligibleShopOrders / OperationalShopCount; null when no operational shop.</summary>
    public decimal? OrdersPerShop { get; set; }
}

/// <summary>Aggregate KPIs for the seller-performance page.</summary>
public class SellerPerformanceTotalsDto
{
    /// <summary>Inclusive calendar-day count in the selected VN date range.</summary>
    public int DayCount { get; set; }

    /// <summary>
    /// Unique VN days where the seller had ≥1 operational shop (union of shop periods).
    /// Totals = Σ across sellers (not a global union).
    /// </summary>
    public int SellerOperationalDays { get; set; }

    /// <summary>Count of active sellers in role Seller.</summary>
    public int ActiveSellerCount { get; set; }

    /// <summary>Active sellers with at least one non-canceled order in the period.</summary>
    public int SellersWithOrders { get; set; }

    /// <summary>
    /// Current Active shops owned by active sellers in scope (informational snapshot).
    /// Not used as a rate denominator.
    /// </summary>
    public int ActiveShopCount { get; set; }

    /// <summary>
    /// Current Deactivated shops owned by active sellers in scope (informational snapshot).
    /// </summary>
    public int DeactivatedShopCount { get; set; }

    /// <summary>
    /// Current Suspended shops owned by active sellers in scope (informational snapshot).
    /// </summary>
    public int SuspendedShopCount { get; set; }

    /// <summary>
    /// Current total shops (Active + Deactivated + Suspended + any other status) in scope.
    /// </summary>
    public int ShopCount { get; set; }

    /// <summary>
    /// Shops with ≥1 operational day intersecting the report range.
    /// </summary>
    public int OperationalShopCount { get; set; }

    /// <summary>
    /// Alias of <see cref="OperationalShopCount"/> (legacy name).
    /// </summary>
    public int ParticipatingShopCount
    {
        get => OperationalShopCount;
        set => OperationalShopCount = value;
    }

    /// <summary>
    /// Σ operational shop-days in the period (per-shop merged intervals, then summed).
    /// </summary>
    public int ShopDays { get; set; }

    /// <summary>
    /// Non-canceled period orders that have a non-null ShopId (numerator for shop averages).
    /// </summary>
    public int EligibleShopOrderCount { get; set; }

    /// <summary>Non-canceled period orders with null ShopId.</summary>
    public int UnassignedOrderCount { get; set; }

    /// <summary>
    /// UnassignedOrderCount / OrderCount × 100. Null when OrderCount is 0.
    /// </summary>
    public decimal? UnassignedOrderRate { get; set; }

    /// <summary>Non-canceled orders in the period (all active sellers; includes null ShopId).</summary>
    public int OrderCount { get; set; }

    /// <summary>Revenue in the period.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Cost in the period (same formula as dashboard KPI).</summary>
    public decimal Cost { get; set; }

    /// <summary>Profit = Revenue − Cost.</summary>
    public decimal Profit { get; set; }

    /// <summary>Profit margin % = Profit / Revenue × 100. Null when Revenue is 0.</summary>
    public decimal? ProfitMargin { get; set; }

    /// <summary>Revenue / OrderCount. Null when OrderCount is 0.</summary>
    public decimal? AverageRevenuePerOrder { get; set; }

    /// <summary>Profit / OrderCount. Null when OrderCount is 0.</summary>
    public decimal? AverageProfitPerOrder { get; set; }

    /// <summary>OrderCount / DayCount (calendar volume rate).</summary>
    public decimal AverageOrdersPerCalendarDay { get; set; }

    /// <summary>
    /// OrderCount / SellerOperationalDays. Null when SellerOperationalDays is 0.
    /// Primary fair seller/day intensity metric.
    /// </summary>
    public decimal? AverageOrdersPerOperationalDay { get; set; }

    /// <summary>
    /// EligibleShopOrderCount / OperationalShopCount (period total per operational shop).
    /// </summary>
    public decimal? AverageOrdersPerOperationalShop { get; set; }

    /// <summary>
    /// EligibleShopOrderCount / ShopDays. Primary shop intensity metric.
    /// </summary>
    public decimal? AverageOrdersPerShopDay { get; set; }

    /// <summary>
    /// Deprecated alias of <see cref="AverageOrdersPerCalendarDay"/>.
    /// </summary>
    public decimal AverageOrdersPerDay
    {
        get => AverageOrdersPerCalendarDay;
        set => AverageOrdersPerCalendarDay = value;
    }

    /// <summary>
    /// Deprecated alias of <see cref="AverageOrdersPerOperationalShop"/>.
    /// </summary>
    public decimal? AverageOrdersPerActiveShop
    {
        get => AverageOrdersPerOperationalShop;
        set => AverageOrdersPerOperationalShop = value;
    }

    /// <summary>
    /// Deprecated alias of <see cref="AverageOrdersPerShopDay"/>.
    /// </summary>
    public decimal? AverageOrdersPerActiveShopPerDay
    {
        get => AverageOrdersPerShopDay;
        set => AverageOrdersPerShopDay = value;
    }
}

/// <summary>One seller performance row.</summary>
public class SellerPerformanceRowDto
{
    /// <summary>Seller user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Non-canceled orders in the period (includes null ShopId).</summary>
    public int Orders { get; set; }

    /// <summary>
    /// Current Active shops whose Shop.OwnerId is this seller (informational snapshot).
    /// </summary>
    public int ActiveShopCount { get; set; }

    /// <summary>
    /// Current Deactivated shops whose Shop.OwnerId is this seller (informational snapshot).
    /// </summary>
    public int DeactivatedShopCount { get; set; }

    /// <summary>
    /// Current Suspended shops whose Shop.OwnerId is this seller (informational snapshot).
    /// </summary>
    public int SuspendedShopCount { get; set; }

    /// <summary>
    /// Current total shops for this seller (all statuses).
    /// </summary>
    public int ShopCount { get; set; }

    /// <summary>Unique VN days with ≥1 operational shop for this seller.</summary>
    public int SellerOperationalDays { get; set; }

    /// <summary>Shops with ≥1 operational day in the period for this seller.</summary>
    public int OperationalShopCount { get; set; }

    /// <summary>Alias of <see cref="OperationalShopCount"/> (legacy).</summary>
    public int ParticipatingShopCount
    {
        get => OperationalShopCount;
        set => OperationalShopCount = value;
    }

    /// <summary>Σ operational shop-days for this seller in the period.</summary>
    public int ShopDays { get; set; }

    /// <summary>Period orders with non-null ShopId for this seller.</summary>
    public int EligibleShopOrderCount { get; set; }

    /// <summary>Period orders with null ShopId for this seller.</summary>
    public int UnassignedOrderCount { get; set; }

    /// <summary>UnassignedOrderCount / Orders × 100. Null when Orders is 0.</summary>
    public decimal? UnassignedOrderRate { get; set; }

    /// <summary>Revenue in the period.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Cost in the period.</summary>
    public decimal Cost { get; set; }

    /// <summary>Profit = Revenue − Cost.</summary>
    public decimal Profit { get; set; }

    /// <summary>Profit margin % = Profit / Revenue × 100. Null when Revenue is 0.</summary>
    public decimal? ProfitMargin { get; set; }

    /// <summary>Revenue / Orders. Null when Orders is 0.</summary>
    public decimal? AverageRevenuePerOrder { get; set; }

    /// <summary>Profit / Orders. Null when Orders is 0.</summary>
    public decimal? AverageProfitPerOrder { get; set; }

    /// <summary>Orders / DayCount.</summary>
    public decimal AverageOrdersPerCalendarDay { get; set; }

    /// <summary>Orders / SellerOperationalDays.</summary>
    public decimal? AverageOrdersPerOperationalDay { get; set; }

    /// <summary>EligibleShopOrderCount / OperationalShopCount.</summary>
    public decimal? AverageOrdersPerOperationalShop { get; set; }

    /// <summary>EligibleShopOrderCount / ShopDays.</summary>
    public decimal? AverageOrdersPerShopDay { get; set; }

    /// <summary>Deprecated alias of <see cref="AverageOrdersPerCalendarDay"/>.</summary>
    public decimal AverageOrdersPerDay
    {
        get => AverageOrdersPerCalendarDay;
        set => AverageOrdersPerCalendarDay = value;
    }

    /// <summary>Deprecated alias of <see cref="AverageOrdersPerOperationalShop"/>.</summary>
    public decimal? AverageOrdersPerActiveShop
    {
        get => AverageOrdersPerOperationalShop;
        set => AverageOrdersPerOperationalShop = value;
    }

    /// <summary>Deprecated alias of <see cref="AverageOrdersPerShopDay"/>.</summary>
    public decimal? AverageOrdersPerActiveShopPerDay
    {
        get => AverageOrdersPerShopDay;
        set => AverageOrdersPerShopDay = value;
    }
}
