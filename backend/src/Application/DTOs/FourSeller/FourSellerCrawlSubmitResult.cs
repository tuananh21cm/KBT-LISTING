namespace Application.DTOs.FourSeller;

/// <summary>
/// Result of submitting crawl URLs to 4Seller.
/// </summary>
public class FourSellerCrawlSubmitResult
{
    /// <summary>
    /// Drafts already present on 4Seller (returned as objects in <c>data</c>).
    /// Claim these immediately — do not treat as re-scrape confirm.
    /// </summary>
    public List<FourSellerMoveDraftRecordDto> ExistingDrafts { get; set; } = [];

    /// <summary>True when 4Seller accepted URLs into the crawl pool (<c>data: []</c>).</summary>
    public bool AcceptedToPool { get; set; }

    /// <summary>Raw JSON body from 4Seller crawl submit (first response).</summary>
    public string? RawResponse { get; set; }
}
