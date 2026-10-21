using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Syncs TikTok return / refund cases from 4Seller onto local orders.
///
/// Sole owner of <c>Order.PlatformOrderStatus = REFUND</c>, <c>Order.RefundedAt</c> and
/// <c>Order.CancelReason</c>: the order list API reports a refunded order under a plain commerce
/// status and never says whether the case is still open, closed, or cancelled.
/// </summary>
public interface IOrderAfterSalesSyncService
{
    /// <summary>
    /// Asks 4Seller to pull fresh return cases from TikTok Shop for every logged-in seller.
    /// 4Seller does the work asynchronously, so results land on a later <see cref="PullAsync"/>.
    /// </summary>
    /// <param name="ownerUserId">Limit to one seller; null = every logged-in seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OrderAfterSalesSyncResultDto> TriggerPlatformPullAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads return cases from 4Seller and applies them to local orders, then notifies ops about
    /// the orders that actually changed.
    /// </summary>
    /// <param name="ownerUserId">Limit to one seller; null = every logged-in seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OrderAfterSalesSyncResultDto> PullAsync(
        Guid? ownerUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One-shot maintenance pass: re-reads every return case each seller has (no watermark) and
    /// makes local <c>REFUND</c> mirror that list exactly. Fixes three classes of stale state an
    /// incremental pull can no longer reach, because its watermark sits at today and 4Seller keeps
    /// no local copy of the cases:
    ///
    /// <list type="bullet">
    /// <item>Orders with a case that are not in <c>REFUND</c>, or whose clock was stamped from
    /// <c>platformUpdateTime</c> — the last time the platform touched the case — which drifts forward
    /// and sorts nothing like TikTok Shop / 4Seller.</item>
    /// <item>Orders in <c>REFUND</c> that no case mentions at all, left behind by the first version
    /// of this flow deriving refunds from <c>cancelReason</c> on the order list.</item>
    /// <item>Orders in <c>REFUND</c> with no refund clock, which render as an empty date and sort to
    /// the tail of the refund tab.</item>
    /// </list>
    ///
    /// Runs at most once per deployment (marked by
    /// <see cref="Shared.Constants.AfterSalesSyncSettingKeys.ReturnStatusBackfill"/>), sends
    /// no Telegram, and leaves the incremental pull watermark untouched.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OrderAfterSalesSyncResultDto> ReconcileAfterSalesFullScanAsync(
        CancellationToken cancellationToken = default);
}
