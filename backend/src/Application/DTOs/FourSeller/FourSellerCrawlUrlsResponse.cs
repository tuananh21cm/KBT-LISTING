namespace Application.DTOs.FourSeller;

/// <summary>
/// Response after submitting crawl URLs (creates local ownership batch).
/// </summary>
public class FourSellerCrawlUrlsResponse
{
    /// <summary>Local scrape batch id.</summary>
    public Guid BatchId { get; set; }

    /// <summary>Whether 4Seller accepted the request.</summary>
    public bool Accepted { get; set; }

    /// <summary>Number of URLs submitted.</summary>
    public int Count { get; set; }

    /// <summary>Raw JSON response from 4Seller get-crawl-url-repeat.</summary>
    public string? FourSellerResponse { get; set; }
}
