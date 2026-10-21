namespace Application.DTOs.Shop;

/// <summary>
/// Shop response DTO (ops + TikTok profile). On hold amounts are on <see cref="ShopFinanceDto"/>, not here.
/// </summary>
public class ShopDto
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

    /// <summary>UTC timestamp when the shop was connected to 4Seller.</summary>
    public DateTime? FourSellerConnectedAt { get; set; }

    /// <summary>Region.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Currency.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Shop URL.</summary>
    public string? ShopUrl { get; set; }

    /// <summary>Metadata JSON.</summary>
    public string? Metadata { get; set; }

    /// <summary>Proxy IP:port.</summary>
    public string? Proxy { get; set; }

    /// <summary>Browser profile name (đã login).</summary>
    public string? ProfileName { get; set; }

    /// <summary>Shop account email.</summary>
    public string? Email { get; set; }

    /// <summary>TikTok account password.</summary>
    public string? TiktokPassword { get; set; }

    /// <summary>Days live.</summary>
    public string? DayLive { get; set; }

    /// <summary>DELI metric (settlement period in days; TTS sync or manual).</summary>
    public int? Deli { get; set; }

    /// <summary>New phone number.</summary>
    public string? NewPhoneNumber { get; set; }

    /// <summary>Old bank account number.</summary>
    public string? OldBankNumber { get; set; }

    /// <summary>New bank account number.</summary>
    public string? NewBankNumber { get; set; }

    /// <summary>Payment recipient name (tên thanh toán tiền).</summary>
    public string? PaymentName { get; set; }

    /// <summary>Linked receiving bank account id.</summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>Linked receiving bank account display name.</summary>
    public string? ReceivingBankAccountName { get; set; }

    /// <summary>Linked receiving bank account number.</summary>
    public string? ReceivingBankAccountNumber { get; set; }

    /// <summary>Linked receiving bank account holder name.</summary>
    public string? ReceivingBankAccountHolderName { get; set; }

    /// <summary>Linked receiving bank provider source name (PingPongX, …).</summary>
    public string? ReceivingBankSourceName { get; set; }

    /// <summary>Linked receiving bank provider source account name.</summary>
    public string? ReceivingBankSourceAccountName { get; set; }

    /// <summary>Linked receiving bank account currency.</summary>
    public string? ReceivingBankAccountCurrency { get; set; }

    /// <summary>Shop ownership type: Per (cá nhân) or Llc (công ty).</summary>
    public string? ShopType { get; set; }

    /// <summary>Operational TikTok categories (parent or leaf). Distinct from TTS ProductCategories.</summary>
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

    /// <summary>Fashion style codes (Shein Style facet: elegant/casual/vintage/sexy/boho/cute/glamorous/vacation/modest/party).</summary>
    public List<string> StyleCodes { get; set; } = [];

    /// <summary>LLC company name.</summary>
    public string? LlcName { get; set; }

    /// <summary>Two-factor authentication secret.</summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>Display order (lower = earlier).</summary>
    public int SortOrder { get; set; }

    /// <summary>TikTok published listing count (<c>publish_limit.published_num</c>); alias of <see cref="PublishLimitPublished"/>.</summary>
    public int ActiveListingCount { get; set; }

    /// <summary>TikTok shop code.</summary>
    public string? ShopCode { get; set; }

    /// <summary>TikTok shop_status (ops Status is derived from seller.status / shop_status on TTS sync).</summary>
    public int? TikTokShopStatus { get; set; }

    /// <summary>Timezone.</summary>
    public string? Timezone { get; set; }

    /// <summary>TikTok create time (UTC).</summary>
    public DateTime? TikTokCreatedAt { get; set; }

    /// <summary>Verified documents count.</summary>
    public int? VerifiedDocuments { get; set; }

    /// <summary>Email verified on TikTok.</summary>
    public bool? EmailVerified { get; set; }

    /// <summary>Phone verified on TikTok.</summary>
    public bool? PhoneVerified { get; set; }

    /// <summary>seller_type (1 CB / 2 POP).</summary>
    public int? SellerType { get; set; }

    /// <summary>business_type raw.</summary>
    public int? BusinessType { get; set; }

    /// <summary>Pay entity type.</summary>
    public int? PayEntityType { get; set; }

    /// <summary>EIN / cert number.</summary>
    public string? CompanyCertNum { get; set; }

    /// <summary>Cert type.</summary>
    public int? CompanyCertType { get; set; }

    /// <summary>Account / passport name.</summary>
    public string? AccountName { get; set; }

    /// <summary>is_blocked.</summary>
    public bool? IsBlocked { get; set; }

    /// <summary>is_blocking.</summary>
    public bool? IsBlocking { get; set; }

    /// <summary>TikTok published listing count.</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>TikTok total listing limit.</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>
    /// Total online seconds today (Vietnam calendar) from <c>ShopOnlineSessions</c>.
    /// Open sessions count through UTC now.
    /// </summary>
    public long TodayOnlineSeconds { get; set; }

    /// <summary>Last TikTok profile sync (UTC).</summary>
    public DateTime? TikTokProfileSyncedAt { get; set; }

    /// <summary>Health Center AHR score.</summary>
    public int? AhrScore { get; set; }

    /// <summary>Shop Performance Score (0–5).</summary>
    public decimal? ShopPerformanceScore { get; set; }

    /// <summary>SPS — Product satisfaction (0–5).</summary>
    public decimal? ProductSatisfactionScore { get; set; }

    /// <summary>SPS — Fulfillment and logistics (0–5).</summary>
    public decimal? FulfillmentScore { get; set; }

    /// <summary>SPS — Customer service (0–5).</summary>
    public decimal? CustomerServiceScore { get; set; }

    /// <summary>Last TikTok shop score sync (UTC).</summary>
    public DateTime? TikTokShopScoreSyncedAt { get; set; }

    /// <summary>Company address.</summary>
    public ShopAddressDto? CompanyAddress { get; set; }

    /// <summary>Owner person.</summary>
    public ShopOwnerPersonDto? OwnerPerson { get; set; }

    /// <summary>Shipping services.</summary>
    public List<ShopLogisticsServiceDto> LogisticsServices { get; set; } = [];

    /// <summary>Product categories.</summary>
    public List<ShopProductCategoryDto> ProductCategories { get; set; } = [];

    /// <summary>Masked TikTok settlement bank account (not receiving-bank NewBankNumber).</summary>
    public string? SettlementBankAccountNumber { get; set; }

    /// <summary>Settlement payment method.</summary>
    public string? SettlementPaymentMethod { get; set; }

    /// <summary>Settlement beneficiary name.</summary>
    public string? SettlementBeneficiaryName { get; set; }

    /// <summary>Settlement account type.</summary>
    public string? SettlementAccountType { get; set; }

    /// <summary>Settlement account status.</summary>
    public int? SettlementAccountStatus { get; set; }

    /// <summary>Per-shop auto publish enabled.</summary>
    public bool AutoPublishEnabled { get; set; }

    /// <summary>Seconds between auto publishes (null = system default).</summary>
    public int? AutoPublishIntervalSeconds { get; set; }

    /// <summary>Minimum draft age in minutes before pick (null = system default).</summary>
    public int? AutoPublishMinDraftAgeMinutes { get; set; }

    /// <summary>Aggregate scheduler status: processing | waiting (null = no badge).</summary>
    public string? AutoPublishSchedulerStatus { get; set; }

    /// <summary>UTC when scheduler may pick the next draft (countdown target).</summary>
    public DateTime? AutoPublishNextPickAt { get; set; }

    /// <summary>Eligible draft count in FIFO (for Queue +N display).</summary>
    public int? AutoPublishEligibleDraftCount { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at (UTC).</summary>
    public DateTime? UpdatedAt { get; set; }
}
