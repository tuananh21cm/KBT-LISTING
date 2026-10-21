namespace Application.DTOs.FourSeller;

/// <summary>
/// TikTok brand option from 4Seller get-brand-list / brand-suggest by shop.
/// </summary>
public class FourSellerBrandDto
{
    /// <summary>TikTok brand ID (`brandId`).</summary>
    public string BrandId { get; set; } = string.Empty;

    /// <summary>Original brand name (`originalName`).</summary>
    public string? OriginalName { get; set; }

    /// <summary>Display name (`displayName`).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional status from 4Seller.</summary>
    public string? Status { get; set; }
}
