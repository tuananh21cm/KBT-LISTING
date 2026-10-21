namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Result of classifying a 4Seller <c>/api/user/is-login</c> response.
/// Only <see cref="Expired"/> means the session is confirmed dead;
/// <see cref="Unverified"/> covers network / WAF / transient API errors and must NOT
/// latch the cookie status to expired.
/// </summary>
public enum FourSellerSessionState
{
    /// <summary>HTTP 2xx, <c>code:0</c>, <c>data:true</c>.</summary>
    LoggedIn,

    /// <summary>HTTP 2xx, <c>code:0</c>, and <c>data</c> is false or missing — session confirmed invalid.</summary>
    Expired,

    /// <summary>
    /// Cannot decide: non-2xx, non-JSON body, <c>code != 0</c>, EdgeOne 567, proxy/timeout.
    /// Callers must keep the previous cookie status.
    /// </summary>
    Unverified
}
