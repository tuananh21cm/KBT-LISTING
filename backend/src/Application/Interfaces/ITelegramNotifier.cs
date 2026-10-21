using Application.DTOs.Order;
using Application.DTOs.Shein;
using Domain.Entities;

namespace Application.Interfaces;

/// <summary>
/// Sends Telegram notifications for orders (settings-gated).
/// </summary>
public interface ITelegramNotifier
{
    /// <summary>
    /// Notifies for newly inserted orders. Skips when shop is unresolved or already stamped.
    /// Sets <c>TelegramNewOrderNotifiedAt</c> on successful send — caller must <c>SaveChanges</c>.
    /// Failures are logged; does not throw.
    /// </summary>
    /// <param name="orders">Orders with items loaded.</param>
    /// <param name="shopNameFallbacks">Optional shop display name when <see cref="Order.ShopId"/> is null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Telegram messages sent.</returns>
    Task<int> NotifyNewOrdersAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies for orders that changed to canceled/cancelled. Failures are logged; does not throw.
    /// </summary>
    /// <param name="orders">Orders with items loaded.</param>
    /// <param name="shopNameFallbacks">Optional shop display name when <see cref="Order.ShopId"/> is null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyCanceledOrdersAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the after-sales group about orders whose return / refund state changed on this
    /// sync run. Gated by <c>fourseller.after_sales.notify_enabled</c> and sent to
    /// <c>fourseller.after_sales.telegram_chat_id</c>. Failures are logged; does not throw.
    /// </summary>
    /// <param name="notices">Changed orders (items loaded) with their return case context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Messages actually sent.</returns>
    Task<int> NotifyAfterSalesOrdersAsync(
        IReadOnlyList<OrderAfterSalesNotice> notices,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the daily status summary when enabled and due for the current local hour.
    /// Returns true when a message was sent.
    /// </summary>
    Task<bool> TrySendDailySummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the daily per-seller Listed/Order/Revenue summary when enabled and due.
    /// Returns true when a message was sent.
    /// </summary>
    Task<bool> TrySendSellerSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the daily fulfil / purchase processing summary when enabled and due
    /// (ops chat: cancel/refund / USPS / kho / fulfil).
    /// Returns true when a message was sent.
    /// </summary>
    Task<bool> TrySendFulfilSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies ops chat when orders first need USPS and/or Shein warehouse handling.
    /// Merges both signals into one message per order. Sets <c>TelegramUspsNotifiedAt</c> /
    /// <c>TelegramWarehouseNotifiedAt</c> on successful send — caller must <c>SaveChanges</c>.
    /// Failures are logged; does not throw.
    /// </summary>
    /// <param name="orders">Orders with items + supplier purchases loaded when possible.</param>
    /// <param name="shopNameFallbacks">Optional shop display name when <see cref="Order.ShopId"/> is null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Telegram messages sent.</returns>
    Task<int> NotifyFulfilHandlingAsync(
        IReadOnlyList<Order> orders,
        IReadOnlyDictionary<Guid, string?>? shopNameFallbacks = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the Add track chat for orders the backend SHEIN detail sweep just gave real carrier
    /// tracking to (or deferred from a prior pass), reporting the Auto Mark Ship outcome.
    /// Items whose Mark Ship is still pending stamp <c>TelegramAddTrackNotifyPendingAt</c> and are
    /// not sent (so the message never implies the platform already has the code). Caller must
    /// <c>SaveChanges</c> when the change tracker is dirty. Gated by
    /// <c>shein.order_detail_sync.notify_carrier_tracking</c>. Failures are logged; does not throw.
    /// </summary>
    /// <param name="items">Candidates collected during the sweep, after Mark Ship ran.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Telegram messages sent.</returns>
    Task<int> NotifySheinDetailAddTrackAsync(
        IReadOnlyList<SheinDetailAddTrackNotifyItem> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies ops chat when an order local message (Thông báo) is saved with non-empty content.
    /// Failures are logged; does not throw. No-op when message is empty/cleared.
    /// </summary>
    /// <param name="order">Order with items loaded when possible.</param>
    /// <param name="shopNameFallback">Optional shop display name when <see cref="Order.ShopId"/> is null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyOrderMessageAsync(
        Order order,
        string? shopNameFallback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a daily ops digest of Purchased-tab orders that still have no last-mile tracking
    /// after the configured N-hour threshold. Uses the same VN local clock as the fulfil summary.
    /// Failures are logged; does not throw.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of orders listed in the digest (0 when skipped or none matched).</returns>
    Task<int> TryNotifyStaleNoTrackingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries new-order Telegram for recent orders still missing <c>TelegramNewOrderNotifiedAt</c>
    /// once <c>ShopId</c> is resolved (reclaim / late link). Expires unresolved rows past 24h.
    /// Failures are logged; does not throw. Persists stamps itself.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of Telegram messages sent.</returns>
    Task<int> TrySendPendingNewOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an ops alert about background job health — a sync loop overdue past its threshold,
    /// a background loop that crashed and restarted, or recovery from either. Goes to
    /// <c>telegram.cancel_refund_chat_id</c>. Failures are logged; does not throw.
    /// </summary>
    /// <param name="text">Message body. Already HTML-escaped by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when Telegram accepted the message.</returns>
    Task<bool> NotifySyncHealthAlertAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Force-sends the seller summary for a VN local calendar day (ignores schedule / last-sent).
    /// Still requires Telegram credentials. Used by Development diagnostics.
    /// </summary>
    /// <param name="summaryLocalDate">Local calendar date at 00:00 (Unspecified).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ForceSendSellerSummaryForLocalDateAsync(
        DateTime summaryLocalDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one sample "Add track · ✅ Thành công (TEST)" message to
    /// <c>shein.order_detail_sync.telegram_chat_id</c>. Does not require the notify toggle and does
    /// not stamp any order. Used to verify BotToken + Chat ID routing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when Telegram accepted the message.</returns>
    Task<bool> ForceSendSheinDetailAddTrackTestAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the TikTok shop profile alert chat when email/mobile/bank changed
    /// or settlement bank last-4 does not match any receiving bank. Failures are logged; does not throw.
    /// </summary>
    /// <param name="kind">Alert kind: <c>email</c>, <c>mobile</c>, <c>bank</c>, <c>bank_last4</c>.</param>
    /// <param name="sellerDisplayName">Shop owner display name.</param>
    /// <param name="profileName">Browser profile name.</param>
    /// <param name="shopName">Shop display name.</param>
    /// <param name="oldValue">Previous value (may be null for last4-only alerts).</param>
    /// <param name="newValue">New value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyShopProfileAlertAsync(
        string kind,
        string sellerDisplayName,
        string? profileName,
        string? shopName,
        string? oldValue,
        string? newValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the TikTok shop alert chat when the ops <c>Shop.Status</c> changed
    /// (Active / Deactivated / Suspended). Failures are logged; does not throw.
    /// </summary>
    /// <param name="shop">Shop after the change (Platform loaded when available).</param>
    /// <param name="previousStatus">Ops status before the change.</param>
    /// <param name="sellerDisplayName">Shop owner display name.</param>
    /// <param name="changeSource">What triggered it: <c>tiktok_sync</c>, <c>admin</c>, <c>seller</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyShopStatusChangeAsync(
        Shop shop,
        string previousStatus,
        string sellerDisplayName,
        string changeSource,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies when a new TikTok Shop payout is synced into the receiving-bank ledger.
    /// Failures are logged; does not throw.
    /// </summary>
    /// <param name="sellerDisplayName">Shop owner display name.</param>
    /// <param name="profileName">Browser profile name.</param>
    /// <param name="shopName">Shop display name.</param>
    /// <param name="amount">Payout amount.</param>
    /// <param name="currency">Currency code (e.g. USD).</param>
    /// <param name="occurredAtUtc">Payment success time (UTC).</param>
    /// <param name="paymentId">TikTok payment_id (ledger ExternalTransactionId).</param>
    /// <param name="receivingCardSourceName">Receiving card provider (e.g. PingPongX).</param>
    /// <param name="receivingCardMaskedNumber">Masked account number (e.g. 0039****9978).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task NotifyNewTikTokPayoutAsync(
        string sellerDisplayName,
        string? profileName,
        string? shopName,
        decimal amount,
        string currency,
        DateTime occurredAtUtc,
        string paymentId,
        string? receivingCardSourceName,
        string? receivingCardMaskedNumber,
        CancellationToken cancellationToken = default);
}
