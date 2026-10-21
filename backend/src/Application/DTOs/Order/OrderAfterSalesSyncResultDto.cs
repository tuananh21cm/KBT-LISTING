namespace Application.DTOs.Order;

/// <summary>
/// Request body for a manual after-sales sync run.
/// </summary>
public class OrderAfterSalesSyncRequest
{
    /// <summary>Optional: only this seller's cookie. Null = every logged-in seller.</summary>
    public Guid? OwnerUserId { get; set; }
}

/// <summary>
/// Result of one after-sales (return / refund) sync run across sellers.
/// </summary>
public class OrderAfterSalesSyncResultDto
{
    /// <summary>Sellers whose returns were read successfully.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>Return cases read from 4Seller.</summary>
    public int ReturnsRead { get; set; }

    /// <summary>Local orders whose after-sales state changed this run.</summary>
    public int OrdersUpdated { get; set; }

    /// <summary>
    /// Orders put in <c>REFUND</c> this run — a case that is complete or still running, since being
    /// listed on 4Seller's returns page is what puts an order in the refund tab.
    /// </summary>
    public int Refunded { get; set; }

    /// <summary>Orders whose refund was released because the return request closed.</summary>
    public int RefundReverted { get; set; }

    /// <summary>
    /// Orders stripped of <c>REFUND</c> because no return case mentions them at all (full scan only).
    /// </summary>
    public int RefundWithoutCaseReleased { get; set; }

    /// <summary>
    /// Orders in <c>REFUND</c> with no return case that had to be left alone: their raw 4Seller
    /// payload carries no commerce status to restore.
    /// </summary>
    public int RefundWithoutCaseStuck { get; set; }

    /// <summary>
    /// Live cases carrying neither <c>platformCreateTime</c> nor <c>platformUpdateTime</c>. Their
    /// orders keep the commerce status rather than entering <c>REFUND</c> with no clock.
    /// </summary>
    public int CasesWithoutClock { get; set; }

    /// <summary>Return cases whose order is not in our database (other channel / outside sync window).</summary>
    public int UnmatchedReturns { get; set; }

    /// <summary>Telegram messages queued for ops.</summary>
    public int Notified { get; set; }

    /// <summary>True when another 4Seller sync held the gate and this run did nothing.</summary>
    public bool Skipped { get; set; }

    /// <summary>Per-seller failures (cookie expired, API error, …).</summary>
    public List<OrderSyncSellerFailureDto> FailedSellers { get; set; } = [];
}
