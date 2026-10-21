namespace Application.DTOs.Shop;

/// <summary>
/// Shop option for browser extension Product Scraper (Active + connected to 4Seller).
/// </summary>
public class ScraperShopOptionDto
{
    /// <summary>Local shop id.</summary>
    public Guid Id { get; set; }

    /// <summary>4Seller internal shop id (for postHtml shopIdList).</summary>
    public long FourSellerShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Browser profile name attached to the shop.</summary>
    public string? ProfileName { get; set; }

    /// <summary>Platform code (e.g. TikTok).</summary>
    public string PlatformCode { get; set; } = string.Empty;

    /// <summary>Platform avatar URL.</summary>
    public string? PlatformAvatarUrl { get; set; }

    /// <summary>Region / marketplace (e.g. US).</summary>
    public string? Region { get; set; }

    /// <summary>TTS-synced product categories (label + value).</summary>
    public List<ShopProductCategoryDto> ProductCategories { get; set; } = [];

    /// <summary>Operational TikTok categories (parent or leaf).</summary>
    public List<ShopCatalogCategoryDto> Categories { get; set; } = [];

    /// <summary>First operational category ID (denormalized).</summary>
    public string? CategoryId { get; set; }

    /// <summary>Joined display names for <see cref="Categories"/>.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Target age lower bound.</summary>
    public int? AgeMin { get; set; }

    /// <summary>Target age upper bound.</summary>
    public int? AgeMax { get; set; }

    /// <summary>Target price lower bound (USD).</summary>
    public decimal? PriceMinUsd { get; set; }

    /// <summary>Target price upper bound (USD).</summary>
    public decimal? PriceMaxUsd { get; set; }

    /// <summary>Prefer QuickShip products.</summary>
    public bool PreferQuickShip { get; set; }

    /// <summary>Prefer local-warehouse products.</summary>
    public bool PreferLocal { get; set; }

    /// <summary>Prefer US warehouse products.</summary>
    public bool PreferUsWarehouse { get; set; }

    /// <summary>Prefer Shein Club products.</summary>
    public bool PreferSheinClub { get; set; }

    /// <summary>Minimum product rating (0–5). 0 = do not filter.</summary>
    public decimal MinRating { get; set; }

    /// <summary>Minimum sold count. 0 = do not filter.</summary>
    public int MinSoldCount { get; set; }

    /// <summary>Keep products with strictly fewer variants than this. 0 = do not filter.</summary>
    public int MaxVariantCount { get; set; }

    /// <summary>Target body/size group code (regular/plus/midsize/curvy/petite/tall).</summary>
    public string? BodySizeCode { get; set; }

    /// <summary>Fashion style codes (multi-select whitelist).</summary>
    public List<string> StyleCodes { get; set; } = [];

    /// <summary>Local listings with siteStatus=active.</summary>
    public int ActiveCount { get; set; }
}
