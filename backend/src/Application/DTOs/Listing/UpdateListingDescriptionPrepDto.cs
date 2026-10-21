namespace Application.DTOs.Listing;

/// <summary>
/// Lightweight local-only persist after AI Rewrite / Insert Images (no 4Seller push).
/// </summary>
public class UpdateListingDescriptionPrepDto
{
    /// <summary>Description HTML to store on the listing.</summary>
    public string DescriptionHtml { get; set; } = string.Empty;

    /// <summary>Whether AI Rewrite has been applied.</summary>
    public bool DescriptionRewritten { get; set; }

    /// <summary>Whether Insert Images ran after the latest Rewrite (optional).</summary>
    public bool DescriptionImagesInserted { get; set; }

    /// <summary>
    /// SEO product title from the same Rewrite. Null leaves the stored title untouched
    /// (Insert Images sends description only).
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// Backend TikTok search terms. Null leaves the column untouched (Insert Images);
    /// a list (including empty) overwrites.
    /// </summary>
    public List<string>? SearchTerms { get; set; }

    /// <summary>
    /// PDP product highlights. Null leaves the column untouched; a list (including empty)
    /// overwrites.
    /// </summary>
    public List<string>? ProductHighlights { get; set; }
}

/// <summary>Result of a description-prep local save.</summary>
public class ListingDescriptionPrepResultDto
{
    /// <summary>Listing id.</summary>
    public Guid ListingId { get; set; }

    /// <summary>Persisted description HTML.</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>Persisted Rewrite flag.</summary>
    public bool DescriptionRewritten { get; set; }

    /// <summary>Persisted Insert Images flag.</summary>
    public bool DescriptionImagesInserted { get; set; }

    /// <summary>Persisted product title.</summary>
    public string? ProductName { get; set; }

    /// <summary>Persisted search terms when this prep call wrote them.</summary>
    public List<string>? SearchTerms { get; set; }

    /// <summary>Persisted product highlights when this prep call wrote them.</summary>
    public List<string>? ProductHighlights { get; set; }
}
