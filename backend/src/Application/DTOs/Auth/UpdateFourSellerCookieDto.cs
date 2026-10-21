namespace Application.DTOs.Auth;

/// <summary>
/// Request to set or clear the current user's 4Seller session cookie.
/// Empty or whitespace clears the cookie; non-empty replaces it.
/// </summary>
public class UpdateFourSellerCookieDto
{
    /// <summary>
    /// Session cookie copied from a browser logged into 4Seller (should include userToken).
    /// </summary>
    public string? FourSellerCookie { get; set; }
}
