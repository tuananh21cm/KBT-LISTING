namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of push save-draft / publish to 4Seller.
/// </summary>
public class FourSellerListingPushResultDto
{
    /// <summary>Whether the API accepted the request.</summary>
    public bool Success { get; set; }

    /// <summary>Message from 4Seller or local mapping.</summary>
    public string? Message { get; set; }

    /// <summary>Listing id returned or echoed.</summary>
    public long? ListingId { get; set; }

    /// <summary>TikTok product id when present in response.</summary>
    public string? TikTokProductId { get; set; }

    /// <summary>Raw response body (truncated if large).</summary>
    public string? RawResponse { get; set; }
}
