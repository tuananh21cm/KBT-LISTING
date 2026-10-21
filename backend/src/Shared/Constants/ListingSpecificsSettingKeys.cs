namespace Shared.Constants;

/// <summary>
/// Setting keys for Auto Specifics (managed via admin Settings).
/// </summary>
public static class ListingSpecificsSettingKeys
{
    /// <summary>
    /// When true, Auto Specifics stays clickable after Specifics are already filled
    /// (beyond Brand / cert). When false (default), the button locks once mapped —
    /// avoids a second paid map-specifics call by accident.
    /// </summary>
    public const string AllowRepeat = "listing.specifics.allow_repeat";

    /// <summary>Default: lock Auto Specifics after the first successful map.</summary>
    public const string DefaultAllowRepeat = "false";
}
