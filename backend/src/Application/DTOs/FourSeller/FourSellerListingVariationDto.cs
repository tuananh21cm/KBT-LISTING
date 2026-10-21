namespace Application.DTOs.FourSeller;

/// <summary>
/// One variation / SKU row from a 4Seller listing page or detail payload.
/// </summary>
public class FourSellerListingVariationDto
{
    /// <summary>4Seller variation id.</summary>
    public long Id { get; set; }

    /// <summary>Variant primary thumbnail (may be pipe-separated).</summary>
    public string? MainImage { get; set; }

    /// <summary>All image URLs for this variation (detail gallery / sku images).</summary>
    public List<string> ImageUrls { get; set; } = [];

    /// <summary>Variant title (e.g. Color/Size).</summary>
    public string? Title { get; set; }

    /// <summary>Seller SKU.</summary>
    public string? SellerSku { get; set; }

    /// <summary>Original / sale price.</summary>
    public decimal? OriginalPrice { get; set; }

    /// <summary>Global price when present.</summary>
    public decimal? GlobalPrice { get; set; }

    /// <summary>Available stock.</summary>
    public int? AvailableStock { get; set; }
}
