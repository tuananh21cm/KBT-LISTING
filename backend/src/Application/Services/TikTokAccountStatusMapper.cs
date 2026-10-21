using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Maps TikTok seller-us account status onto Kinhbac ops <c>Shop.Status</c>
/// (Active / Deactivated / Suspended).
/// </summary>
public static class TikTokAccountStatusMapper
{
    /// <summary>TTS <c>seller.status</c> / <c>shop_status</c> — shop đang hoạt động.</summary>
    public const int Active = 2;

    /// <summary>TTS <c>seller.status</c> / <c>shop_status</c> — đã deactivated.</summary>
    public const int Deactivated = 3;

    /// <summary>TTS <c>shop_status</c> — shop suspended/closed (ops map → Deactivated).</summary>
    public const int Closed = 4;

    /// <summary>
    /// Worse-of <paramref name="sellerStatus"/> (<c>seller.status</c>) and
    /// <paramref name="shopStatus"/> (<c>seller.shop_status</c>): if either is
    /// Deactivated(3) or Closed(4) → ops <c>Deactivated</c>.
    /// <paramref name="identityVideoVerificationLock"/> wins: always <c>Suspended</c>.
    /// </summary>
    /// <returns>
    /// Next ops status, or <c>null</c> when the payload has no usable code or the current
    /// shop is <c>Suspended</c> and TTS still reports active (do not unsuspend).
    /// </returns>
    public static string? ResolveOpsStatus(
        int? sellerStatus,
        int? shopStatus,
        string? currentStatus,
        bool identityVideoVerificationLock = false)
    {
        if (identityVideoVerificationLock)
        {
            return ShopStatuses.Suspended;
        }

        if (IsDeactivatedOrClosed(sellerStatus) || IsDeactivatedOrClosed(shopStatus))
        {
            return ShopStatuses.Deactivated;
        }

        var code = sellerStatus ?? shopStatus;
        if (code is not Active)
        {
            return null;
        }

        if (string.Equals(currentStatus, ShopStatuses.Suspended, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ShopStatuses.Active;
    }

    private static bool IsDeactivatedOrClosed(int? code) =>
        code is Deactivated or Closed;
}
