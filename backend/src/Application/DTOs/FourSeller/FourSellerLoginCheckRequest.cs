namespace Application.DTOs.FourSeller;

/// <summary>
/// Optional cookie to validate without saving. When omitted, uses the user's stored cookie.
/// </summary>
public class FourSellerLoginCheckRequest
{
    /// <summary>
    /// Draft cookie from the UI. When null/empty, checks <c>ApplicationUser.FourSellerCookie</c>.
    /// </summary>
    public string? FourSellerCookie { get; set; }
}
