namespace Shared.Constants;

/// <summary>
/// Setting keys for listing source-rating color thresholds on admin ListingCard.
/// </summary>
public static class ListingRatingSettingKeys
{
    /// <summary>Rating ≥ this value → success (green stars). Default 4.4.</summary>
    public const string SuccessMin = "listing.rating.success_min";

    /// <summary>
    /// Rating ≥ this and &lt; success_min → warning (yellow stars).
    /// Below this → danger (red stars). Default 4.0.
    /// </summary>
    public const string WarningMin = "listing.rating.warning_min";
}
