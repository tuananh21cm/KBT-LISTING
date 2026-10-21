namespace Application.DTOs.Auth;

/// <summary>
/// Raw 4Seller session cookie (internal / admin tooling only — never expose via public API).
/// </summary>
public class FourSellerCookieDto
{
    /// <summary>
    /// Full 4Seller cookie string (semicolon-separated name=value pairs, includes userToken).
    /// </summary>
    public string FourSellerCookie { get; set; } = string.Empty;

    /// <summary>
    /// Persisted 4Seller login status: none / unchecked / logged_in / expired.
    /// </summary>
    public string FourSellerCookieLoginStatus { get; set; } = "none";
}
