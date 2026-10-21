using Application.DTOs.Order;

namespace Application.DTOs.Dashboard;

/// <summary>
/// Lightweight admin dashboard snapshot (aggregates only).
/// </summary>
public class DashboardSummaryDto
{
    /// <summary>Timezone used for period buckets.</summary>
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Resolved period: day | week | month.</summary>
    public string Period { get; set; } = "day";

    /// <summary>Period start (inclusive), local VN yyyy-MM-dd.</summary>
    public string FromDate { get; set; } = string.Empty;

    /// <summary>Period end (inclusive), local VN yyyy-MM-dd.</summary>
    public string ToDate { get; set; } = string.Empty;

    /// <summary>Period KPIs (VN local window for selected period).</summary>
    public DashboardTodayDto Today { get; set; } = new();

    /// <summary>Order status counts for orders created in the selected period.</summary>
    public OrderStatusCountsDto OrderStatusCounts { get; set; } = new();

    /// <summary>Listing page/site counts for listings dated in the selected period.</summary>
    public DashboardListingsDto Listings { get; set; } = new();

    /// <summary>Shop status snapshot (current inventory — not period-scoped).</summary>
    public DashboardShopsDto Shops { get; set; } = new();

    /// <summary>Product aggregates.</summary>
    public DashboardProductsDto Products { get; set; } = new();

    /// <summary>Seller ranking for the selected period (top N).</summary>
    public List<DashboardSellerRankDto> SellersToday { get; set; } = [];

    /// <summary>Shop revenue / profit ranking for the selected period (top N).</summary>
    public List<DashboardShopFinanceDto> ShopsFinance { get; set; } = [];

    /// <summary>Top-selling products (order line items) for the selected period.</summary>
    public List<DashboardTopProductDto> TopProducts { get; set; } = [];
}

/// <summary>Period window KPIs.</summary>
public class DashboardTodayDto
{
    /// <summary>Local VN end date yyyy-MM-dd (same as summary ToDate).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Orders created in the period: Active shop with receiving bank linked,
    /// excluding CANCELLED / REFUND. Same set as <see cref="Revenue"/>.
    /// </summary>
    public int OrderCount { get; set; }

    /// <summary>
    /// Revenue for the same order set as <see cref="OrderCount"/>
    /// (seller net; Active shop + receiving bank + exclude CANCELLED / REFUND).
    /// </summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// Net fulfil purchase spend in the period from <c>FulfilTransactions</c>
    /// (Debit − Credit, same formula as Lịch sử mua hàng / <c>/admin/fulfil/transactions/stats</c>).
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// Σ <c>FulfilTransactions.CouponAmount</c> in the same UTC window / filters as <see cref="Cost"/>
    /// (snapshot giảm giá coupon theo lần mua CostUpdate; Manual = null → 0).
    /// Không trừ vào Cost — chỉ tổng giảm giá để theo dõi (cột Coupon trên Lịch sử mua hàng).
    /// </summary>
    public decimal Coupon { get; set; }

    /// <summary>Profit = Revenue − Cost for the period.</summary>
    public decimal Profit { get; set; }

    /// <summary>
    /// Markup % = Profit / Cost × 100 (Cost = fulfil purchase spend). Null when Cost is 0.
    /// </summary>
    public decimal? ProfitMargin { get; set; }

    /// <summary>
    /// Net money received in the period from <c>ReceivingBankTransactions</c>
    /// (Confirmed only; Credit − Debit), same formula as receiving-bank stats.
    /// </summary>
    public decimal Income { get; set; }

    /// <summary>
    /// Deprecated alias of <see cref="Cost"/> (kept for older clients). Prefer <c>cost</c>.
    /// </summary>
    public decimal CardSpend { get; set; }

    /// <summary>Published listings created/updated in the period.</summary>
    public int ListedCount { get; set; }
}

/// <summary>Listing aggregate counts.</summary>
public class DashboardListingsDto
{
    /// <summary>Draft pageType count.</summary>
    public int Draft { get; set; }

    /// <summary>Published pageType count.</summary>
    public int Published { get; set; }

    /// <summary>Listings with a non-empty SyncError.</summary>
    public int SyncErrorCount { get; set; }

    /// <summary>Active siteStatus count.</summary>
    public int Active { get; set; }

    /// <summary>Inactive siteStatus count.</summary>
    public int Inactive { get; set; }

    /// <summary>Pending siteStatus count.</summary>
    public int Pending { get; set; }
}

/// <summary>Shop aggregate counts.</summary>
public class DashboardShopsDto
{
    /// <summary>Total shops in scope.</summary>
    public int Total { get; set; }

    /// <summary>Active shops.</summary>
    public int Active { get; set; }

    /// <summary>Inactive shops.</summary>
    public int Inactive { get; set; }

    /// <summary>Suspended shops.</summary>
    public int Suspended { get; set; }

    /// <summary>Shops with FourSellerShopId set.</summary>
    public int Connected { get; set; }
}

/// <summary>Product aggregate counts.</summary>
public class DashboardProductsDto
{
    /// <summary>Total products in scope.</summary>
    public int Total { get; set; }

    /// <summary>Draft products.</summary>
    public int Draft { get; set; }

    /// <summary>Active products.</summary>
    public int Active { get; set; }

    /// <summary>Archived products.</summary>
    public int Archived { get; set; }
}

/// <summary>One seller row for period ranking.</summary>
public class DashboardSellerRankDto
{
    /// <summary>Seller user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Display name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Published listings in the period.</summary>
    public int Listed { get; set; }

    /// <summary>Non-canceled orders in the period.</summary>
    public int Orders { get; set; }

    /// <summary>Revenue in the period.</summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// Sum of order <c>SupplierTotal</c> in the period.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>Profit = Revenue − Cost for the period.</summary>
    public decimal Profit { get; set; }

    /// <summary>Profit margin % = Profit / Revenue × 100. Null when Revenue is 0.</summary>
    public decimal? ProfitMargin { get; set; }
}

/// <summary>One shop row for period revenue / profit comparison.</summary>
public class DashboardShopFinanceDto
{
    /// <summary>Local shop id; null when unassigned.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Shop display name (ProfileName else Name, or Unassigned).</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Shop profile name when set.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Owner seller display name.</summary>
    public string? SellerName { get; set; }

    /// <summary>Non-canceled orders in the period.</summary>
    public int Orders { get; set; }

    /// <summary>Revenue in the period.</summary>
    public decimal Revenue { get; set; }

    /// <summary>Cost (order SupplierTotal sum) in the period.</summary>
    public decimal Cost { get; set; }

    /// <summary>Profit = Revenue − Cost.</summary>
    public decimal Profit { get; set; }
}

/// <summary>One top-selling product row (aggregated order line items).</summary>
public class DashboardTopProductDto
{
    /// <summary>Stable group key (sellerSku, else title).</summary>
    public string ProductKey { get; set; } = string.Empty;

    /// <summary>Seller SKU when available.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Display title (most recent non-empty in group).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Product image URL when available.</summary>
    public string? ImgUrl { get; set; }

    /// <summary>Primary seller display name (highest qty for this product key).</summary>
    public string? SellerName { get; set; }

    /// <summary>Primary shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Primary shop profile name.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Units sold in the period (sum of line quantities).</summary>
    public int QuantitySold { get; set; }

    /// <summary>Distinct orders containing this product.</summary>
    public int OrderCount { get; set; }

    /// <summary>Line revenue = sum(ItemPrice × Quantity).</summary>
    public decimal Revenue { get; set; }
}
