namespace Application.DTOs.FourSeller;

/// <summary>
/// OAuth authorize URL returned from 4Seller.
/// </summary>
public class FourSellerAuthRedirectResponse
{
    /// <summary>TikTok/4Seller OAuth authorize URL for the user to open.</summary>
    public string AuthorizeUrl { get; set; } = string.Empty;
}
