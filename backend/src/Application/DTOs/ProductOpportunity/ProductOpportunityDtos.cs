namespace Application.DTOs.ProductOpportunity;

/// <summary>
/// Extension auto-sync config for Product opportunities (resolved from Settings).
/// </summary>
public class ProductOpportunitySyncConfigDto
{
    /// <summary>Whether auto-sync is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Auto-sync interval in seconds (value+unit resolved).</summary>
    public int IntervalSeconds { get; set; }
}

/// <summary>
/// One TikTok <c>category_infos</c> entry for lead/list (level is 1-indexed API value).
/// </summary>
public class ProductOpportunityCategoryInfoDto
{
    /// <summary>TikTok category external id (<c>cate_id</c>).</summary>
    public long CateId { get; set; }

    /// <summary>TikTok API level = <c>TikTokCategory.Level + 1</c>.</summary>
    public int Level { get; set; }
}

/// <summary>
/// Niche + shop match for the extension scrape (abort when <see cref="HasCatalog"/> is false).
/// </summary>
public class ProductOpportunitySyncContextDto
{
    /// <summary>Matched local shop id (null when shop not found — service throws instead).</summary>
    public Guid ShopId { get; set; }

    /// <summary>True when at least one catalog category resolved to TikTokCategories.</summary>
    public bool HasCatalog { get; set; }

    /// <summary>
    /// First catalog category as a single TikTok <c>category_infos</c> entry (backward compatible).
    /// Prefer <see cref="CategoryInfoGroups"/> when present.
    /// </summary>
    public List<ProductOpportunityCategoryInfoDto> CategoryInfos { get; set; } = [];

    /// <summary>
    /// One scrape group per shop catalog category (selected node only).
    /// Extension scrapes each group separately then merges (OR) — never concatenates
    /// sibling/parent leaves into one TikTok <c>category_infos</c> path.
    /// </summary>
    public List<List<ProductOpportunityCategoryInfoDto>> CategoryInfoGroups { get; set; } = [];

    /// <summary>Display names of catalog categories that resolved to a scrape path.</summary>
    public List<string> CatalogNames { get; set; } = [];
}

/// <summary>
/// Batch of trending-keyword leads scraped by the browser extension.
/// </summary>
public class SyncProductOpportunitiesDto
{
    /// <summary>Run id from extension unified Sync (optional).</summary>
    public string? RunId { get; set; }

    /// <summary>TikTok seller / OEC id — matches <c>Shop.ExternalShopId</c>.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>Alias of <see cref="ExternalShopId"/> (extension may send sellerId).</summary>
    public string? SellerId { get; set; }

    /// <summary>Mapped lead rows (already field-stripped).</summary>
    public List<SyncProductOpportunityItemDto> Items { get; set; } = [];
}

/// <summary>One TikTok product-opportunity lead (minimal fields).</summary>
public class SyncProductOpportunityItemDto
{
    /// <summary>TikTok <c>lead_id</c>.</summary>
    public string LeadId { get; set; } = string.Empty;

    /// <summary>Keyword / product phrase.</summary>
    public string LeadName { get; set; } = string.Empty;

    /// <summary>First image URL.</summary>
    public string? PicUrl { get; set; }

    /// <summary>All image URLs from TikTok <c>pic_url</c> (max 8).</summary>
    public List<string> PicUrls { get; set; } = [];

    /// <summary>Parsed search volume.</summary>
    public int SearchVolume { get; set; }

    /// <summary>Parsed online-product count when numeric.</summary>
    public int? OnlineProducts { get; set; }

    /// <summary>Original string when not numeric (e.g. <c>&lt; 10</c>).</summary>
    public string? OnlineProductsRaw { get; set; }

    /// <summary>Search-volume ring ratio.</summary>
    public string? SearchVolumeRingRatio { get; set; }

    /// <summary>Online-products ring ratio.</summary>
    public string? OnlineProductsRingRatio { get; set; }

    /// <summary>Parsed order count when numeric.</summary>
    public int? OrderCount { get; set; }

    /// <summary>Original order string when not numeric.</summary>
    public string? OrderRaw { get; set; }

    /// <summary>Order ring ratio.</summary>
    public string? OrderRingRatio { get; set; }

    /// <summary>Parsed GMV when numeric.</summary>
    public decimal? Gmv { get; set; }

    /// <summary>Original GMV string when not numeric.</summary>
    public string? GmvRaw { get; set; }

    /// <summary>GMV ring ratio.</summary>
    public string? GmvRingRatio { get; set; }

    /// <summary>TikTok <c>categories[]</c> (max 20).</summary>
    public List<ProductOpportunityCategoryDto> Categories { get; set; } = [];

    /// <summary>L1 category id.</summary>
    public string? Level1CateId { get; set; }

    /// <summary>L1 category name.</summary>
    public string? Level1CateName { get; set; }

    /// <summary>L2 category id.</summary>
    public string? Level2CateId { get; set; }

    /// <summary>L2 category name.</summary>
    public string? Level2CateName { get; set; }

    /// <summary>L3 category id.</summary>
    public string? Level3CateId { get; set; }

    /// <summary>L3 category name.</summary>
    public string? Level3CateName { get; set; }

    /// <summary>Tag codes (<c>hot_search</c>, <c>low_competition</c>, …).</summary>
    public List<string> TagCodes { get; set; } = [];

    /// <summary>Display labels from TikTok <c>lead_demands[].lead_tag_text</c> (max 8).</summary>
    public List<string> LeadTagTexts { get; set; } = [];

    /// <summary>TikTok <c>lead_source_tag</c>.</summary>
    public string? LeadSourceTag { get; set; }

    /// <summary>TikTok <c>lead_cluster_source_tag</c>.</summary>
    public string? LeadClusterSourceTag { get; set; }

    /// <summary>TikTok <c>opportunity_type</c>.</summary>
    public int? OpportunityType { get; set; }
}

/// <summary>One TikTok category on a product-opportunity lead.</summary>
public class ProductOpportunityCategoryDto
{
    /// <summary>TikTok <c>cate_id</c>.</summary>
    public string CateId { get; set; } = string.Empty;

    /// <summary>TikTok <c>cate_name</c>.</summary>
    public string CateName { get; set; } = string.Empty;

    /// <summary>TikTok category level when present.</summary>
    public int? Level { get; set; }
}

/// <summary>Result of a canonical-keyword + per-shop link sync.</summary>
public class SyncProductOpportunitiesResultDto
{
    /// <summary>Matched shop id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>New canonical keyword rows (new <c>lead_id</c>).</summary>
    public int Inserted { get; set; }

    /// <summary>Shop links after this sync (payload size after lead_id dedupe).</summary>
    public int Linked { get; set; }

    /// <summary>Payload <c>lead_id</c> that already had a canonical row.</summary>
    public int Skipped { get; set; }
}

/// <summary>Query filters for the product-opportunity list.</summary>
public class ProductOpportunityListQuery
{
    /// <summary>1-based page (alias of <see cref="PageCurrent"/>).</summary>
    public int? Page { get; set; }

    /// <summary>1-based page.</summary>
    public int PageCurrent { get; set; } = 1;

    /// <summary>Page size.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Filter by local shop.</summary>
    public Guid? ShopId { get; set; }

    /// <summary>Admin seller filter (ignored / overwritten on seller API).</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>Alias of <see cref="OwnerId"/>.</summary>
    public Guid? OwnerUserId
    {
        get => OwnerId;
        set
        {
            if (value.HasValue)
            {
                OwnerId = value;
            }
        }
    }

    /// <summary>Keyword search on lead name.</summary>
    public string? Q { get; set; }

    /// <summary>Alias of <see cref="Q"/>.</summary>
    public string? Search
    {
        get => Q;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Q = value;
            }
        }
    }

    /// <summary>When true, only snapshot rows whose keyword is saved for the filtered shop (or a linked shop in scope).</summary>
    public bool? Saved { get; set; }

    /// <summary>
    /// Filter rows whose <c>leadTagTexts</c> contain any of these labels (OR).
    /// Also accepts CSV in <see cref="LeadTag"/>.
    /// </summary>
    public List<string>? LeadTags { get; set; }

    /// <summary>CSV or single <c>lead_tag_text</c> (alias of <see cref="LeadTags"/>).</summary>
    public string? LeadTag { get; set; }

    /// <summary><c>volume</c> (default) / <c>online</c> / <c>order</c> / <c>gmv</c> / <c>name</c> / <c>synced</c>.</summary>
    public string? Sort { get; set; }

    /// <summary><c>desc</c> (default for volume/online/synced) / <c>asc</c> (default for name).</summary>
    public string? SortDir { get; set; }
}

/// <summary>Paged product-opportunity list.</summary>
public class ProductOpportunityListPageDto
{
    /// <summary>Rows.</summary>
    public List<ProductOpportunityListItemDto> Items { get; set; } = [];

    /// <summary>Total matching.</summary>
    public int Total { get; set; }

    /// <summary>Current page.</summary>
    public int PageCurrent { get; set; }

    /// <summary>Page size.</summary>
    public int PageSize { get; set; }

    /// <summary>Latest <c>Shops.ProductOpportunitiesSyncedAt</c> in the filtered shop set.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// Distinct <c>leadTagTexts</c> in the shop/owner/q/saved scope (not narrowed by <c>leadTag</c>).
    /// </summary>
    public List<string> LeadTagOptions { get; set; } = [];
}

/// <summary>One snapshot row for the dense table.</summary>
public class ProductOpportunityListItemDto
{
    /// <summary>Row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Shop id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Shop display name.</summary>
    public string ShopName { get; set; } = string.Empty;

    /// <summary>Browser profile name.</summary>
    public string? ShopProfileName { get; set; }

    /// <summary>Shop owner id.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner display name.</summary>
    public string? OwnerFullName { get; set; }

    /// <summary>Owner username.</summary>
    public string? OwnerUserName { get; set; }

    /// <summary>TikTok lead id.</summary>
    public string LeadId { get; set; } = string.Empty;

    /// <summary>Keyword.</summary>
    public string LeadName { get; set; } = string.Empty;

    /// <summary>Thumbnail (first image).</summary>
    public string? PicUrl { get; set; }

    /// <summary>All image URLs.</summary>
    public List<string> PicUrls { get; set; } = [];

    /// <summary>Search volume.</summary>
    public int SearchVolume { get; set; }

    /// <summary>Online product count when numeric.</summary>
    public int? OnlineProducts { get; set; }

    /// <summary>Raw online-products when not numeric.</summary>
    public string? OnlineProductsRaw { get; set; }

    /// <summary>Search-volume ring ratio.</summary>
    public string? SearchVolumeRingRatio { get; set; }

    /// <summary>Online-products ring ratio.</summary>
    public string? OnlineProductsRingRatio { get; set; }

    /// <summary>Order count when numeric.</summary>
    public int? OrderCount { get; set; }

    /// <summary>Raw order when not numeric.</summary>
    public string? OrderRaw { get; set; }

    /// <summary>Order ring ratio.</summary>
    public string? OrderRingRatio { get; set; }

    /// <summary>GMV when numeric.</summary>
    public decimal? Gmv { get; set; }

    /// <summary>Raw GMV when not numeric.</summary>
    public string? GmvRaw { get; set; }

    /// <summary>GMV ring ratio.</summary>
    public string? GmvRingRatio { get; set; }

    /// <summary>TikTok categories.</summary>
    public List<ProductOpportunityCategoryDto> Categories { get; set; } = [];

    /// <summary>L1 category id.</summary>
    public string? Level1CateId { get; set; }

    /// <summary>L1 category name.</summary>
    public string? Level1CateName { get; set; }

    /// <summary>L2 category id.</summary>
    public string? Level2CateId { get; set; }

    /// <summary>L2 category name.</summary>
    public string? Level2CateName { get; set; }

    /// <summary>L3 category id.</summary>
    public string? Level3CateId { get; set; }

    /// <summary>L3 category name.</summary>
    public string? Level3CateName { get; set; }

    /// <summary>Display labels from <c>lead_demands[].lead_tag_text</c>.</summary>
    public List<string> LeadTagTexts { get; set; } = [];

    /// <summary>Lead source tag.</summary>
    public string? LeadSourceTag { get; set; }

    /// <summary>Lead cluster source tag.</summary>
    public string? LeadClusterSourceTag { get; set; }

    /// <summary>Opportunity type.</summary>
    public int? OpportunityType { get; set; }

    /// <summary>Shop last successful snapshot time (representative shop when unfiltered).</summary>
    public DateTime? ShopSyncedAt { get; set; }

    /// <summary>
    /// How many shops in the current owner/shop scope link this keyword.
    /// <c>1</c> when <c>shopId</c> is filtered.
    /// </summary>
    public int ShopCount { get; set; } = 1;

    /// <summary>True when this keyword is saved for the row's shop (or any linked shop in the current filter).</summary>
    public bool IsSaved { get; set; }
}

/// <summary>Request to delete selected snapshot keywords.</summary>
public class BulkDeleteProductOpportunitiesRequest
{
    /// <summary>Canonical keyword ids (max 200).</summary>
    public List<Guid> Ids { get; set; } = [];

    /// <summary>
    /// When set, only unlink this shop (seller must own it).
    /// Admin omit = delete the canonical keyword and all shop links.
    /// </summary>
    public Guid? ShopId { get; set; }
}

/// <summary>Result of a bulk delete.</summary>
public class BulkDeleteProductOpportunitiesResultDto
{
    /// <summary>How many rows were removed.</summary>
    public int Deleted { get; set; }
}

/// <summary>Save / unsave a snapshot keyword for one shop.</summary>
public class SetProductOpportunitySavedRequest
{
    /// <summary>True to save; false to remove from that shop's saved pool.</summary>
    public bool Saved { get; set; }

    /// <summary>
    /// Shop to bookmark. Required when the keyword is linked to more than one shop;
    /// omit to use the representative linked shop.
    /// </summary>
    public Guid? ShopId { get; set; }
}

/// <summary>Save / unsave a keyword by text (no ProductOpportunity row — audience suggest).</summary>
public class SetSavedKeywordRequest
{
    /// <summary>Local shop id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>Search keyword to bookmark.</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>True to save; false to remove from the saved pool.</summary>
    public bool Saved { get; set; }
}

/// <summary>Result of save / unsave.</summary>
public class ProductOpportunitySavedResultDto
{
    /// <summary>Whether the keyword is now saved for the target shop.</summary>
    public bool Saved { get; set; }
}

/// <summary>Light saved-keyword list for the Product Scraper (search Shein by bookmarked keywords).</summary>
public class SavedKeywordListDto
{
    /// <summary>Saved keywords for this shop only (bookmark <c>ShopId</c> match).</summary>
    public List<SavedKeywordItemDto> Items { get; set; } = [];
}

/// <summary>One saved keyword — no heavy snapshot metrics.</summary>
public class SavedKeywordItemDto
{
    /// <summary>Keyword text.</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Thumbnail (snapshot first, else bookmark).</summary>
    public string? PicUrl { get; set; }

    /// <summary>TikTok lead tags from the current snapshot when present.</summary>
    public List<string> LeadTagTexts { get; set; } = [];

    /// <summary>Search volume from the current snapshot (0 when no snapshot row).</summary>
    public int SearchVolume { get; set; }

    /// <summary>Vietnamese niche rationale from saved «Shop này bán gì» when the keyword was analyzed.</summary>
    public string? Rationale { get; set; }

    /// <summary>Short fit label from the same analysis.</summary>
    public string? Fit { get; set; }

    /// <summary>
    /// Shop-research section when this keyword is in saved «Shop này bán gì»:
    /// <c>listNow</c> (List ngay) or <c>upcoming</c> (Đón đầu). Null if not in that analysis.
    /// </summary>
    public string? ResearchLane { get; set; }

    /// <summary>
    /// Important Shein title phrases (at most 2 demand modifiers + primary garment; includes synonym <c>anyOf</c>).
    /// Coverage gate: garment + <see cref="MinDistinctiveHits"/> distinctive hits.
    /// </summary>
    public List<KeywordMatchPhraseDto> MustPhrases { get; set; } = [];

    /// <summary>Hardware / style / extra garments; title score uses weighted coverage.</summary>
    public List<KeywordMatchPhraseDto> ShouldPhrases { get; set; } = [];

    /// <summary>Minimum distinctive hits for the Shein title gate (<c>ceil(N/2)</c>).</summary>
    public int MinDistinctiveHits { get; set; }
}
