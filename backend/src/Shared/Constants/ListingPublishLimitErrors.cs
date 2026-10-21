namespace Shared.Constants;

/// <summary>TikTok / 4Seller listing quota error tokens and shop block reasons.</summary>
public static class ListingPublishLimitErrors
{
    /// <summary>Stored on <c>Shop.AutoPublishBlockReason</c> after Day_Limit.</summary>
    public const string DayLimitBlockReason = "day_limit";

    /// <summary>Opaque token in 4Seller / TikTok failed messages.</summary>
    public const string DayLimitToken = "Day_Limit";

    /// <summary>Full 4Seller code often shown as 【Publish Failed】Listing.Back.Day_Limit.</summary>
    public const string ListingBackDayLimit = "Listing.Back.Day_Limit";

    /// <summary>Whether a failure message indicates daily / back-day listing quota.</summary>
    public static bool IsDayLimit(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains(ListingBackDayLimit, StringComparison.OrdinalIgnoreCase)
               || message.Contains(DayLimitToken, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Shop-capacity failures must not permanently skip the listing.</summary>
    public static bool IsShopCapacityFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (IsDayLimit(message))
        {
            return true;
        }

        return message.Contains("hết slot", StringComparison.OrdinalIgnoreCase)
               || message.Contains("bị chặn auto publish", StringComparison.OrdinalIgnoreCase);
    }
}
