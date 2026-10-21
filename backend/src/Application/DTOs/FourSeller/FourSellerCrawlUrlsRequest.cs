namespace Application.DTOs.FourSeller;

/// <summary>
/// Request to submit product source URLs for 4Seller crawl.
/// </summary>
public class FourSellerCrawlUrlsRequest
{
    /// <summary>Product page URLs to scrape.</summary>
    public List<string> Urls { get; set; } = [];
}
