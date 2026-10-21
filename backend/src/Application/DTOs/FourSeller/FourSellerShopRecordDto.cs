namespace Application.DTOs.FourSeller;

/// <summary>
/// A shop record from 4Seller shop list API.
/// </summary>
public class FourSellerShopRecordDto
{
    /// <summary>4Seller internal shop ID.</summary>
    public long Id { get; set; }

    /// <summary>Platform code on 4Seller (e.g. TIKTOK).</summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>Shop name on platform.</summary>
    public string PlatformShopName { get; set; } = string.Empty;

    /// <summary>Full shop name on 4Seller.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Marketplace / selling partner ID.</summary>
    public string SellingPartnerId { get; set; } = string.Empty;

    /// <summary>Connection status description.</summary>
    public string StatusDesc { get; set; } = string.Empty;

    /// <summary>Marketplace ID (e.g. US).</summary>
    public string MarketplaceId { get; set; } = string.Empty;

    /// <summary>Shop currency from 4Seller (e.g. USD).</summary>
    public string? Currency { get; set; }

    /// <summary>Created time on 4Seller.</summary>
    public string? CreateTime { get; set; }
}
