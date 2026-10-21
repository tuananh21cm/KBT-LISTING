using System.Linq.Expressions;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Orders;

namespace Application.Services;

/// <summary>Pure, testable eligibility/idempotency rules for SHEIN automatic Mark Ship.</summary>
public static class OrderAutoMarkShipPolicy
{
    /// <summary>Minimum delay before retrying a transient 4Seller failure.</summary>
    public static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Legacy Auto Mark Ship error when SHEIN had a last-mile <c>shipping_no</c> but
    /// <c>track_info.name</c> was still empty. Must not stay terminal — name often arrives later.
    /// </summary>
    public const string MissingCarrierError =
        "SHEIN không trả carrier; cần chọn Shipping Service thủ công.";

    /// <summary>
    /// After an unconfirmed Mark Ship, wait this long before treating "still no remote tracking"
    /// as safe to downgrade to <see cref="AutoMarkShipStatuses.Failed"/> (retryable).
    /// TikTok/4Seller often lag &gt;10 minutes; reconcile long-polls before this fires.
    /// </summary>
    public static readonly TimeSpan UnconfirmedGracePeriod = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Short poll when Mark Ship runs without <c>AutoMarkShipPollInteractive</c>
    /// (legacy Cloudflare-safe budget). Extension HTTP no longer opts into Mark Ship.
    /// </summary>
    public const int CloudflareMarkShipPollAttempts = 15;

    /// <summary>Delay between short Mark Ship poll attempts.</summary>
    public const int CloudflareMarkShipPollDelayMs = 1000;

    /// <summary>
    /// Longer poll for Sync Track / detail sweep (~60s) — closer to manual Mark Ship.
    /// </summary>
    public const int InteractiveMarkShipPollAttempts = 30;

    /// <summary>Delay between interactive Mark Ship poll attempts.</summary>
    public const int InteractiveMarkShipPollDelayMs = 2000;

    /// <summary>Resolve publish-poll budget: interactive (Sync Track/sweep) vs short non-interactive.</summary>
    public static (int Attempts, int DelayMs) ResolveMarkShipPoll(bool interactive) =>
        interactive
            ? (InteractiveMarkShipPollAttempts, InteractiveMarkShipPollDelayMs)
            : (CloudflareMarkShipPollAttempts, CloudflareMarkShipPollDelayMs);

    /// <summary>
    /// True only when 4Seller clearly rejected Mark Ship. Pending / timeout / empty/"success"
    /// messages stay unconfirmed so <c>requestId</c> is kept for reconcile.
    /// </summary>
    public static bool IsExplicitMarkShipReject(string? status, string? message)
    {
        if (!string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (message.Equals("success", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Synthetic / ambiguous timeout text from our poller must not clear requestId.
        if (message.Contains("Hết thời gian chờ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <c>SyncFromShein</c> should run Auto Mark Ship reconcile + publish.
    /// Extension HTTP / realtime leave both unset → ingest only; detail sweep / Sync Track opt in.
    /// </summary>
    public static bool ShouldRunAutoMarkShip(
        int? maxAutoMarkShipAttempts,
        bool autoMarkShipPollInteractive) =>
        maxAutoMarkShipAttempts.HasValue || autoMarkShipPollInteractive;

    /// <summary>
    /// Fallback attempt cap when a Mark Ship caller omits an explicit budget but still opted in
    /// (e.g. <c>AutoMarkShipPollInteractive</c> only). Extension HTTP leaves
    /// <c>MaxAutoMarkShipAttempts</c> null and does <b>not</b> run Auto Mark Ship at all —
    /// Mark Ship belongs to the detail sweep / Sync Track.
    /// </summary>
    public const int MaxAttemptsPerSyncRequest = 2;

    /// <summary>Wall-clock budget for the auto Mark Ship phase when an explicit attempt budget is low/zero.</summary>
    public static readonly TimeSpan MaxDurationPerSyncRequest = TimeSpan.FromSeconds(45);

    /// <summary>Hard ceiling on a caller-supplied attempt budget, so one batch still terminates.</summary>
    public const int MaxAttemptsOverride = 50;

    /// <summary>Observed worst case for one Mark Ship publish + poll cycle.</summary>
    private static readonly TimeSpan PerAttemptAllowance = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Attempts allowed for one batch. Detail sweep / Sync Track pass an explicit value
    /// (up to <c>max_mark_ship_per_pass</c>). Null falls back to <see cref="MaxAttemptsPerSyncRequest"/>
    /// only when the caller already opted into Mark Ship (interactive flag).
    ///
    /// An explicit <c>0</c> means "publish nothing this batch" — the sweep passes it once the
    /// pass has spent its Mark Ship budget, and the remaining links defer to the next pass.
    /// </summary>
    public static int ResolveAttemptBudget(int? requested) =>
        requested.HasValue
            ? Math.Clamp(requested.Value, 0, MaxAttemptsOverride)
            : MaxAttemptsPerSyncRequest;

    /// <summary>
    /// Wall-clock budget for a caller-supplied attempt budget. Callers that raise the attempt
    /// count (detail sweep) get scaled time; low/zero budgets stay at
    /// <see cref="MaxDurationPerSyncRequest"/>.
    /// </summary>
    public static TimeSpan ResolveDurationBudget(int? requestedAttempts)
    {
        if (requestedAttempts is null or <= 0)
        {
            return MaxDurationPerSyncRequest;
        }

        var scaled = PerAttemptAllowance * ResolveAttemptBudget(requestedAttempts);
        return scaled > MaxDurationPerSyncRequest ? scaled : MaxDurationPerSyncRequest;
    }

    /// <summary>
    /// Normalizes punctuation, casing, and whitespace while preserving exact alphanumeric
    /// identity. This intentionally does not perform fuzzy matching.
    /// </summary>
    public static string NormalizeCarrier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Concat(value.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));
    }

    /// <summary>Safe exact match after normalization.</summary>
    public static bool IsExactCarrierMatch(string? supplierCarrier, string? fourSellerCarrier)
    {
        var left = NormalizeCarrier(supplierCarrier);
        return left.Length > 0 && left == NormalizeCarrier(fourSellerCarrier);
    }

    /// <summary>
    /// True when Auto Mark Ship was permanently blocked solely because SHEIN had not yet
    /// published <c>track_info.name</c> (carrier display name required for 4Seller Shipping Service).
    /// </summary>
    public static bool IsMissingCarrierBlock(SupplierPurchase link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!string.Equals(link.AutoMarkShipStatus, AutoMarkShipStatuses.Blocked, StringComparison.Ordinal))
        {
            return false;
        }

        var error = link.AutoMarkShipError ?? string.Empty;
        return error.Contains(MissingCarrierError, StringComparison.Ordinal)
            || error.Contains("thiếu carrier", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reopens a missing-carrier <c>blocked</c> row to <c>pending</c> so a later sync can Mark Ship
    /// once <see cref="SupplierPurchase.Carrier"/> is filled. Returns true when status changed.
    /// </summary>
    public static bool TryReopenMissingCarrierWait(SupplierPurchase link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!IsMissingCarrierBlock(link)) return false;

        link.AutoMarkShipStatus = AutoMarkShipStatuses.Pending;
        link.AutoMarkShipError = null;
        link.AutoMarkShipAttemptedAt = null;
        link.AutoMarkShipRequestId = null;
        link.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Whether a previous Mark Ship needs platform reconcile before any new publish attempt.
    /// Any dangling <see cref="SupplierPurchase.AutoMarkShipRequestId"/> qualifies (including
    /// legacy <c>blocked</c> rows and crash-mid-poll <c>unconfirmed</c>).
    /// </summary>
    public static bool NeedsReconcile(SupplierPurchase link)
    {
        return !string.IsNullOrWhiteSpace(link.AutoMarkShipRequestId);
    }

    /// <summary>
    /// Downgrade a dangling unconfirmed Mark Ship so a new publish may run.
    /// Does <b>not</b> stamp <see cref="SupplierPurchase.AutoMarkShipAttemptedAt"/> — keeps
    /// Auto cooldown measured from the original attempt (grace ≥ cooldown → same-pass retry).
    /// </summary>
    public static void DowngradeUnconfirmedToFailed(SupplierPurchase link, string? error = null)
    {
        ArgumentNullException.ThrowIfNull(link);
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Failed;
        link.AutoMarkShipRequestId = null;
        if (!string.IsNullOrWhiteSpace(error))
        {
            var text = error.Trim();
            link.AutoMarkShipError = text.Length <= 1000 ? text : text[..1000];
        }

        link.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Manual Đẩy track may clear unconfirmed and POST <c>to-ship-mark-ship</c> again when
    /// reconcile already confirmed the platform has no carrier tracking (<paramref name="confirmedAbsent"/>,
    /// even inside grace) or Auto would treat the attempt as <paramref name="safeToRetry"/>
    /// (past grace / explicit reject). Must stay false when the remote order could not be read.
    /// </summary>
    public static bool AllowManualClearAndRepublish(bool confirmedAbsent, bool safeToRetry) =>
        confirmedAbsent || safeToRetry;

    /// <summary>
    /// Whether SHEIN carrier tracking should be published via 4Seller Mark Ship.
    /// Only when the platform order still has <b>no</b> published tracking — including joint
    /// packages that just received carrier tracking from SHEIN. Never re-marks an order that
    /// already has <see cref="Order.TrackingNo"/>.
    /// </summary>
    public static bool ShouldAttempt(Order order, SupplierPurchase link, DateTime now)
    {
        // Already published on the platform order (manual or prior auto) — never Mark Ship again.
        if (!string.IsNullOrWhiteSpace(order.TrackingNo))
        {
            return false;
        }

        // TikTok / platform shipping — TikTok fulfills; never to-ship-mark-ship.
        if (TikTokSellerNetCalculator.IsTikTokPlatformShipping(order.ShippingMethod))
        {
            return false;
        }

        // Never Mark Ship cancelled / TikTok refund orders.
        if (PlatformOrderStatuses.IsNonRevenue(order.PlatformOrderStatus))
        {
            return false;
        }

        if (!string.Equals(
                PlatformOrderStatuses.Normalize(order.PlatformOrderStatus),
                PlatformOrderStatuses.AwaitingShipment,
                StringComparison.Ordinal))
        {
            return false;
        }

        // Need real last-mile carrier tracking from SHEIN (warehouse C…/N… codes do not qualify).
        if (link.TrackingKind != SupplierTrackingKinds.Carrier
            || string.IsNullOrWhiteSpace(link.TrackingNo))
        {
            return false;
        }

        // 4Seller Shipping Service needs a carrier name. Prefer persisted Carrier; else allow
        // attempt when tracking prefix alone identifies GOFO/SpeedX/USPS (filled at publish time).
        if (string.IsNullOrWhiteSpace(link.Carrier)
            && string.IsNullOrWhiteSpace(SheinCarrierResolver.InferFromTrackingPrefix(link.TrackingNo)))
        {
            return false;
        }

        // Unconfirmed / blocked-with-requestId must reconcile first — never blind republish.
        if (NeedsReconcile(link)
            || string.Equals(
                link.AutoMarkShipStatus, AutoMarkShipStatuses.Unconfirmed, StringComparison.Ordinal))
        {
            return false;
        }

        // New/changed SHEIN tracking on a still-untracked order (incl. joint link just filled).
        if (!string.Equals(
                link.AutoMarkShipTrackingNo, link.TrackingNo, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return link.AutoMarkShipStatus switch
        {
            null or "" or AutoMarkShipStatuses.Pending => true,
            AutoMarkShipStatuses.Failed =>
                !link.AutoMarkShipAttemptedAt.HasValue
                || now - link.AutoMarkShipAttemptedAt.Value >= RetryCooldown,
            _ => false
        };
    }

    /// <summary>
    /// Link còn việc Auto Mark Ship — cùng luật với <see cref="ShouldAttempt"/> cộng
    /// <see cref="NeedsReconcile"/>, expression chạy được trong SQL.
    ///
    /// Dùng bởi job <c>shein.order_detail_sync</c> (hàng đợi Mark Ship) và
    /// <c>GET shein-sync/config</c> → <c>pendingAutoMarkShipBillnos</c> (extension ép POST list
    /// khi fingerprint đứng yên để dữ liệu carrier còn tươi; Mark Ship không chạy trên HTTP
    /// extension). Phải rộng hơn hoặc bằng <see cref="ShouldAttempt"/>: gồm cả
    /// <c>unconfirmed</c> / còn <c>requestId</c> để sweep reconcile.
    ///
    /// Chỉ nhận trạng thái sàn đã chuẩn hoá (<see cref="PlatformOrderStatuses.Normalize"/> chạy ở
    /// đường ghi) vì SQL không so sánh sau khi trim/upper được.
    /// </summary>
    public static Expression<Func<SupplierPurchase, bool>> PendingQueuePredicate(DateTime now)
    {
        var retryAfter = now - RetryCooldown;
        // Exclude TikTok / platform shipping (EF translates ToLower + Contains).
        return l => l.SupplierOrderNo != null
            && l.TrackingKind == SupplierTrackingKinds.Carrier
            && l.TrackingNo != null
            && l.TrackingNo != ""
            && l.Order != null
            && (l.Order.TrackingNo == null || l.Order.TrackingNo == "")
            && l.Order.PlatformOrderStatus == PlatformOrderStatuses.AwaitingShipment
            && (l.Order.ShippingMethod == null
                || l.Order.ShippingMethod == ""
                || (!l.Order.ShippingMethod.ToLower().Contains("tiktok shipping")
                    && !l.Order.ShippingMethod.ToLower().Contains("platform shipping")))
            && (l.AutoMarkShipRequestId != null
                || l.AutoMarkShipStatus == null
                || l.AutoMarkShipStatus == ""
                || l.AutoMarkShipStatus == AutoMarkShipStatuses.Pending
                || l.AutoMarkShipStatus == AutoMarkShipStatuses.Unconfirmed
                || (l.AutoMarkShipStatus == AutoMarkShipStatuses.Blocked
                    && l.AutoMarkShipError != null
                    && (l.AutoMarkShipError.Contains(MissingCarrierError)
                        || l.AutoMarkShipError.Contains("thiếu carrier")))
                || (l.AutoMarkShipStatus == AutoMarkShipStatuses.Failed
                    && (l.AutoMarkShipAttemptedAt == null
                        || l.AutoMarkShipAttemptedAt <= retryAfter)));
    }
}
