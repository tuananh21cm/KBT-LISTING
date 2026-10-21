namespace Application.DTOs.Shop;

/// <summary>
/// Slim shop row for admin list / filters / Excel export.
/// Includes login fields (email/password/2FA) for ops export; LEFT JOIN receiving-bank for last-4 match only (no full account number / On hold amounts).
/// </summary>
public class AdminShopListItemDto
{
    /// <summary>Shop identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Platform identifier.</summary>
    public Guid PlatformId { get; set; }

    /// <summary>Platform code.</summary>
    public string PlatformCode { get; set; } = string.Empty;

    /// <summary>Platform display name.</summary>
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>External shop ID.</summary>
    public string? ExternalShopId { get; set; }

    /// <summary>Shop avatar image URL.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>4seller shop ID.</summary>
    public string? FourSellerShopId { get; set; }

    /// <summary>4Seller connection status (Connected / NotConnected).</summary>
    public string FourSellerConnectionStatus { get; set; } = string.Empty;

    /// <summary>UTC when 4Seller was first linked (null if never connected or legacy row).</summary>
    public DateTime? FourSellerConnectedAt { get; set; }

    /// <summary>Region.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Shop URL.</summary>
    public string? ShopUrl { get; set; }

    /// <summary>Proxy IP:port (search / list hint).</summary>
    public string? Proxy { get; set; }

    /// <summary>Browser profile name.</summary>
    public string? ProfileName { get; set; }

    /// <summary>Shop account email (search / export).</summary>
    public string? Email { get; set; }

    /// <summary>TikTok account password (ops export).</summary>
    public string? TiktokPassword { get; set; }

    /// <summary>TOTP / 2FA secret (ops export).</summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>LLC company name when <see cref="ShopType"/> is Llc.</summary>
    public string? LlcName { get; set; }

    /// <summary>Days live (search).</summary>
    public string? DayLive { get; set; }

    /// <summary>Payment due / DELI indicator (days).</summary>
    public int? Deli { get; set; }

    /// <summary>Phone (search).</summary>
    public string? NewPhoneNumber { get; set; }

    /// <summary>Payment recipient name (search).</summary>
    public string? PaymentName { get; set; }

    /// <summary>Linked receiving bank account id (null = chưa gắn thẻ).</summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>Last 4 digits of TikTok settlement bank (from <c>SettlementBankAccountNumber</c>).</summary>
    public string? SettlementBankLast4 { get; set; }

    /// <summary>Last 4 digits of the linked receiving-bank account number.</summary>
    public string? ReceivingBankLast4 { get; set; }

    /// <summary>
    /// Last-4 match status: <c>Matched</c> | <c>Mismatched</c> | <c>MissingSettlement</c> | <c>MissingReceivingBank</c>.
    /// </summary>
    public string BankMatchStatus { get; set; } = string.Empty;

    /// <summary>Shop ownership type: Per or Llc.</summary>
    public string? ShopType { get; set; }

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

    /// <summary>Display order (lower = earlier).</summary>
    public int SortOrder { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Owner user identifier.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Owner display name.</summary>
    public string OwnerFullName { get; set; } = string.Empty;

    /// <summary>Owner username.</summary>
    public string OwnerUserName { get; set; } = string.Empty;

    /// <summary>TikTok published listing count (<c>publish_limit.published_num</c>); alias of <see cref="PublishLimitPublished"/>.</summary>
    public int ActiveListingCount { get; set; }

    /// <summary>TikTok shop code.</summary>
    public string? ShopCode { get; set; }

    /// <summary>TikTok published listing count.</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>TikTok total listing limit.</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>
    /// Total online seconds today (Vietnam calendar) from <c>ShopOnlineSessions</c>.
    /// Open sessions count through UTC now.
    /// </summary>
    public long TodayOnlineSeconds { get; set; }

    /// <summary>Timezone.</summary>
    public string? Timezone { get; set; }

    /// <summary>seller_type (1 CB / 2 POP).</summary>
    public int? SellerType { get; set; }

    /// <summary>TikTok shop_status.</summary>
    public int? TikTokShopStatus { get; set; }

    /// <summary>Per-shop auto publish enabled.</summary>
    public bool AutoPublishEnabled { get; set; }

    /// <summary>Seconds between auto publishes (shop override).</summary>
    public int? AutoPublishIntervalSeconds { get; set; }

    /// <summary>Minimum draft age in minutes before auto publish pick.</summary>
    public int? AutoPublishMinDraftAgeMinutes { get; set; }

    /// <summary>Aggregate scheduler status: processing | waiting (null = no badge).</summary>
    public string? AutoPublishSchedulerStatus { get; set; }

    /// <summary>UTC when scheduler may pick the next draft (countdown target).</summary>
    public DateTime? AutoPublishNextPickAt { get; set; }

    /// <summary>Eligible draft count in FIFO (for Queue +N display).</summary>
    public int? AutoPublishEligibleDraftCount { get; set; }
}
