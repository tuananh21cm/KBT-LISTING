using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Auto publish slot rules: occupied = TikTok <c>Shop.PublishLimitPublished</c>
/// vs <c>Shop.PublishLimitTotal</c> (cột Listing trên Shops / Shop Tasks).
/// Missing total does not block pick. Local <c>SiteStatus=active</c> is not used —
/// unsynced TTS deletes would otherwise block draft pick.
/// </summary>
public static class ShopListingSlotRules
{
    /// <summary>
    /// Shop is full only when total is known and occupied count has reached it.
    /// </summary>
    public static bool IsFull(int occupiedCount, int? publishLimitTotal) =>
        publishLimitTotal is > 0 && occupiedCount >= publishLimitTotal.Value;

    /// <summary>
    /// Optimistic +1 after 4Seller accepts a publish. Caps at total when known.
    /// TTS profile sync later overwrites with the real published_num.
    /// </summary>
    public static int NextPublishedCount(int? published, int? publishLimitTotal)
    {
        var next = (published ?? 0) + 1;
        if (publishLimitTotal is > 0 && next > publishLimitTotal.Value)
        {
            return publishLimitTotal.Value;
        }

        return next;
    }

    /// <summary>UTC instant of next Vietnam calendar midnight (start of tomorrow VN).</summary>
    public static DateTime NextVietnamMidnightUtc(DateTime utcNow)
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(AppTimeZones.EnsureUtc(utcNow), AppTimeZones.Vietnam);
        var nextDay = DateOnly.FromDateTime(localNow).AddDays(1);
        var (fromUtc, _) = AppTimeZones.GetVietnamDayUtcRange(nextDay);
        return fromUtc;
    }

    /// <summary>True when shop cooldown is still active.</summary>
    public static bool IsBlocked(DateTime? blockedUntil, DateTime utcNow) =>
        blockedUntil is { } until && until > utcNow;
}
