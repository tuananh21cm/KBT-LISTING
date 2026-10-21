namespace Application.DTOs.Shop;

/// <summary>
/// Slim catalog-profile payload returned by PUT …/catalog-profile (no secrets / bank / TTS blob).
/// </summary>
public class ShopCatalogProfileDto
{
    /// <summary>Shop id.</summary>
    public Guid Id { get; set; }

    /// <summary>Operational TikTok categories (parent or leaf).</summary>
    public List<ShopCatalogCategoryDto> Categories { get; set; } = [];

    /// <summary>First category id (compat / denormalized).</summary>
    public string? CategoryId { get; set; }

    /// <summary>Joined display names (compat / list cell).</summary>
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

    /// <summary>Per-shop auto publish enabled.</summary>
    public bool AutoPublishEnabled { get; set; }

    /// <summary>Seconds between auto publishes (null = system default).</summary>
    public int? AutoPublishIntervalSeconds { get; set; }

    /// <summary>Minimum draft age in minutes before pick (null = system default).</summary>
    public int? AutoPublishMinDraftAgeMinutes { get; set; }
}
