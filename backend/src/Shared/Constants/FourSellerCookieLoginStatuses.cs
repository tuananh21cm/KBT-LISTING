namespace Shared.Constants;

/// <summary>
/// Persisted 4Seller session login status on <c>ApplicationUser</c>.
/// </summary>
public static class FourSellerCookieLoginStatuses
{
    /// <summary>No cookie stored.</summary>
    public const string None = "none";

    /// <summary>Cookie stored but not verified via is-login yet.</summary>
    public const string Unchecked = "unchecked";

    /// <summary>Last check confirmed an active 4Seller session.</summary>
    public const string LoggedIn = "logged_in";

    /// <summary>Last check (or outbound probe) found the session invalid/expired.</summary>
    public const string Expired = "expired";
}
