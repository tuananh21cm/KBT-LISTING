using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Canonical TikTok Shop Product opportunity (trending keyword), unique by <see cref="LeadId"/>.
/// Shops attach via <see cref="ShopLinks"/> — sync does not duplicate the keyword row.
/// </summary>
public class ProductOpportunity : BaseEntity
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>TikTok <c>lead_id</c> — unique globally.</summary>
    public string LeadId { get; set; } = string.Empty;

    /// <summary>Keyword / product phrase (<c>lead_name</c>).</summary>
    public string LeadName { get; set; } = string.Empty;

    /// <summary>First image URL from <c>pic_url</c> (compat / thumbnail).</summary>
    public string? PicUrl { get; set; }

    /// <summary>jsonb array of image URLs from <c>pic_url</c> (max 8).</summary>
    public string? PicUrls { get; set; }

    /// <summary>Parsed search volume (commas stripped) for sort.</summary>
    public int SearchVolume { get; set; }

    /// <summary>Parsed online-product count when numeric; null when raw is like <c>&lt; 10</c>.</summary>
    public int? OnlineProducts { get; set; }

    /// <summary>Original online-products string when it could not be parsed as int.</summary>
    public string? OnlineProductsRaw { get; set; }

    /// <summary>Search-volume ring ratio (e.g. <c>+12%</c>).</summary>
    public string? SearchVolumeRingRatio { get; set; }

    /// <summary>Online-products ring ratio.</summary>
    public string? OnlineProductsRingRatio { get; set; }

    /// <summary>Parsed order count when numeric.</summary>
    public int? OrderCount { get; set; }

    /// <summary>Original order string when it could not be parsed as int.</summary>
    public string? OrderRaw { get; set; }

    /// <summary>Order ring ratio.</summary>
    public string? OrderRingRatio { get; set; }

    /// <summary>Parsed GMV when numeric.</summary>
    public decimal? Gmv { get; set; }

    /// <summary>Original GMV string when it could not be parsed as decimal.</summary>
    public string? GmvRaw { get; set; }

    /// <summary>GMV ring ratio.</summary>
    public string? GmvRingRatio { get; set; }

    /// <summary>jsonb array of TikTok categories (<c>cateId</c>, <c>cateName</c>, <c>level</c>).</summary>
    public string? Categories { get; set; }

    /// <summary>L1 TikTok category id.</summary>
    public string? Level1CateId { get; set; }

    /// <summary>L1 TikTok category name.</summary>
    public string? Level1CateName { get; set; }

    /// <summary>L2 TikTok category id.</summary>
    public string? Level2CateId { get; set; }

    /// <summary>L2 TikTok category name.</summary>
    public string? Level2CateName { get; set; }

    /// <summary>L3 TikTok category id.</summary>
    public string? Level3CateId { get; set; }

    /// <summary>L3 TikTok category name.</summary>
    public string? Level3CateName { get; set; }

    /// <summary>jsonb array of tag codes (<c>hot_search</c>, <c>low_competition</c>, …).</summary>
    public string? TagCodes { get; set; }

    /// <summary>jsonb array of display labels from <c>lead_demands[].lead_tag_text</c>.</summary>
    public string? LeadTagTexts { get; set; }

    /// <summary>TikTok <c>lead_source_tag</c>.</summary>
    public string? LeadSourceTag { get; set; }

    /// <summary>TikTok <c>lead_cluster_source_tag</c>.</summary>
    public string? LeadClusterSourceTag { get; set; }

    /// <summary>TikTok <c>opportunity_type</c> (201/202).</summary>
    public int? OpportunityType { get; set; }

    /// <summary>Shops that currently have this keyword in their sync snapshot.</summary>
    public ICollection<ProductOpportunityShopLink> ShopLinks { get; set; } = [];
}
