namespace Application.DTOs.FourSeller;

/// <summary>
/// One scraped product draft on 4Seller move-draft list.
/// </summary>
public class FourSellerMoveDraftRecordDto
{
    /// <summary>4Seller move-draft id.</summary>
    public long Id { get; set; }

    /// <summary>Draft status code from 4Seller.</summary>
    public int Status { get; set; }

    /// <summary>Thumbnail image URL.</summary>
    public string? Image { get; set; }

    /// <summary>Source product URL.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Number of variants.</summary>
    public int VariantCount { get; set; }

    /// <summary>Lowest price.</summary>
    public decimal? LowPrice { get; set; }

    /// <summary>Highest price.</summary>
    public decimal? HighPrice { get; set; }

    /// <summary>Currency code.</summary>
    public string? Currency { get; set; }

    /// <summary>Creator display name.</summary>
    public string? Creator { get; set; }

    /// <summary>Create time string from 4Seller.</summary>
    public string? CreateTime { get; set; }

    /// <summary>Product title.</summary>
    public string? ProductName { get; set; }

    /// <summary>Local owner user id (when served from ownership store).</summary>
    public Guid? OwnedById { get; set; }

    /// <summary>Local owner display name.</summary>
    public string? OwnedByName { get; set; }
}
