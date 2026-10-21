namespace Application.DTOs.Auth;

/// <summary>
/// Per-user result of an admin 4Seller <c>is-login</c> check.
/// </summary>
public class AdminFourSellerLoginCheckItemDto
{
    /// <summary>Target user id.</summary>
    public Guid UserId { get; set; }

    /// <summary>Username.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Whether <c>ApplicationUser.FourSellerCookie</c> is stored (non-empty).
    /// </summary>
    public bool HasCookie { get; set; }

    /// <summary>
    /// True when 4Seller <c>is-login</c> confirms an active session.
    /// </summary>
    public bool IsLoggedIn { get; set; }

    /// <summary>
    /// Persisted login status after this check (<c>none</c> / <c>unchecked</c> / <c>logged_in</c> / <c>expired</c>).
    /// </summary>
    public string FourSellerCookieLoginStatus { get; set; } = "none";

    /// <summary>
    /// Deprecated: stored cookies are no longer deleted on expire.
    /// </summary>
    public bool CookieCleared { get; set; }

    /// <summary>Human-readable status for the UI.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// True when EdgeOne blocked the request by IP (HTTP 567) — session unverifiable, not expired.
    /// </summary>
    public bool Blocked { get; set; }

    /// <summary>
    /// True when the session could not be verified due to a transient cause
    /// (proxy/timeout, HTTP 5xx/429, non-JSON). Callers must NOT mark the cookie as expired.
    /// </summary>
    public bool Unverified { get; set; }
}
