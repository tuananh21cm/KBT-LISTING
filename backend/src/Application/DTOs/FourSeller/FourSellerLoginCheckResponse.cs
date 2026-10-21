namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of validating a 4Seller session via <c>GET /api/user/is-login</c>.
/// </summary>
public class FourSellerLoginCheckResponse
{
    /// <summary>
    /// True when 4Seller returns <c>code:0</c> and <c>data:true</c>.
    /// </summary>
    public bool IsLoggedIn { get; set; }

    /// <summary>
    /// Human-readable status for the UI.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Whether a cookie remains stored after this check.
    /// </summary>
    public bool HasFourSellerCookie { get; set; }

    /// <summary>
    /// Persisted login status after this check (<c>none</c> / <c>unchecked</c> / <c>logged_in</c> / <c>expired</c>).
    /// </summary>
    public string FourSellerCookieLoginStatus { get; set; } = "none";

    /// <summary>
    /// Deprecated: stored cookies are no longer deleted on expire; status is set to <c>expired</c> instead.
    /// Always <c>false</c> for new clients.
    /// </summary>
    public bool CookieCleared { get; set; }

    /// <summary>
    /// True when 4Seller's EdgeOne WAF blocked the request by IP (HTTP 567). The session could
    /// not be verified — callers must NOT mark the cookie as expired; fix the proxy/IP instead.
    /// </summary>
    public bool Blocked { get; set; }

    /// <summary>
    /// True when the session could not be verified due to a transient cause
    /// (proxy/timeout, HTTP 5xx/429, non-JSON body, API <c>code != 0</c>).
    /// Callers must NOT mark the cookie as expired.
    /// </summary>
    public bool Unverified { get; set; }
}
