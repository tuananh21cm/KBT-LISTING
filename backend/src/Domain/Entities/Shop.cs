using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// TikTok Shop US store owned by a seller.
/// </summary>
public class Shop : BaseEntity
{
    /// <summary>
    /// Shop identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owner user identifier.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Platform identifier.
    /// </summary>
    public Guid PlatformId { get; set; }

    /// <summary>
    /// External shop ID from TikTok or 4seller (optional).
    /// </summary>
    public string? ExternalShopId { get; set; }

    /// <summary>
    /// Shop avatar image URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// 4Seller internal shop id (<c>record.Id</c>). Partner / platform shop id goes in <see cref="ExternalShopId"/>.
    /// </summary>
    public string? FourSellerShopId { get; set; }

    /// <summary>
    /// The <see cref="FourSellerShopId"/> this shop had before it was reconnected under another
    /// 4Seller account. That id is what older Orders / Listings still carry, so ownership rematch
    /// stays able to find them no matter whether the reconnect and the owner change were saved
    /// together or in separate requests.
    /// </summary>
    public string? PreviousFourSellerShopId { get; set; }

    /// <summary>
    /// UTC timestamp when the shop was connected to 4Seller.
    /// </summary>
    public DateTime? FourSellerConnectedAt { get; set; }

    /// <summary>
    /// Shop region (default US).
    /// </summary>
    public string Region { get; set; } = "US";

    /// <summary>
    /// Shop currency (default USD).
    /// </summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Shop status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Shop URL.
    /// </summary>
    public string? ShopUrl { get; set; }

    /// <summary>
    /// Raw metadata from external platforms (JSON).
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Proxy IP:port for shop access.
    /// </summary>
    public string? Proxy { get; set; }

    /// <summary>
    /// Browser profile name used to log in to this shop.
    /// </summary>
    public string? ProfileName { get; set; }

    /// <summary>
    /// Shop account email.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// TikTok account password.
    /// </summary>
    public string? TiktokPassword { get; set; }

    /// <summary>
    /// Days the shop has been live (e.g. "954 days").
    /// </summary>
    public string? DayLive { get; set; }

    /// <summary>
    /// DELI metric (settlement period in days). Synced from TikTok
    /// <c>seller_quality_stat.bill_finish_period_in_days</c> when the extension refreshes.
    /// </summary>
    public int? Deli { get; set; }

    /// <summary>
    /// New phone number.
    /// </summary>
    public string? NewPhoneNumber { get; set; }

    /// <summary>
    /// Old bank account number.
    /// </summary>
    public string? OldBankNumber { get; set; }

    /// <summary>
    /// New bank account number.
    /// </summary>
    public string? NewBankNumber { get; set; }

    /// <summary>
    /// Payment recipient name (tên thanh toán tiền).
    /// Kept in sync from <see cref="ReceivingBankAccount"/> when linked.
    /// </summary>
    public string? PaymentName { get; set; }

    /// <summary>
    /// Linked receiving bank account (thẻ nhận tiền). One shop → one account.
    /// </summary>
    public Guid? ReceivingBankAccountId { get; set; }

    /// <summary>
    /// Shop ownership type: Per (cá nhân) or Llc (công ty).
    /// </summary>
    public string? ShopType { get; set; }

    /// <summary>
    /// First operational TikTok category id (denormalized from <see cref="CatalogCategories"/>).
    /// Distinct from <see cref="ProductCategories"/> (TTS-synced shop catalog).
    /// </summary>
    public string? CategoryId { get; set; }

    /// <summary>
    /// Joined display names for <see cref="CatalogCategories"/> (e.g. "Auto · Women / Dresses").
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// Operational TikTok categories JSON array (<c>{ id, name }</c>). Shop 1 → many categories.
    /// </summary>
    public string? CatalogCategories { get; set; }

    /// <summary>
    /// Target audience age lower bound (inclusive), 0–100.
    /// </summary>
    public int? AgeMin { get; set; }

    /// <summary>
    /// Target audience age upper bound (inclusive), 0–100.
    /// </summary>
    public int? AgeMax { get; set; }

    /// <summary>
    /// Target listing price lower bound (USD).
    /// </summary>
    public decimal? PriceMinUsd { get; set; }

    /// <summary>
    /// Target listing price upper bound (USD).
    /// </summary>
    public decimal? PriceMaxUsd { get; set; }

    /// <summary>
    /// Prefer supplier products with QuickShip (<c>isSupportQuickShip</c>).
    /// </summary>
    public bool PreferQuickShip { get; set; }

    /// <summary>
    /// Prefer local-warehouse products (<c>isLocalProduct</c>).
    /// </summary>
    public bool PreferLocal { get; set; }

    /// <summary>
    /// Prefer US warehouse / US stock products.
    /// </summary>
    public bool PreferUsWarehouse { get; set; }

    /// <summary>
    /// Prefer Shein Club products. False = do not filter.
    /// </summary>
    public bool PreferSheinClub { get; set; }

    /// <summary>
    /// Minimum product rating (0–5). 0 = do not filter.
    /// </summary>
    public decimal MinRating { get; set; }

    /// <summary>
    /// Minimum sold count. 0 = do not filter.
    /// </summary>
    public int MinSoldCount { get; set; }

    /// <summary>
    /// Keep products with strictly fewer variants than this. 0 = do not filter.
    /// </summary>
    public int MaxVariantCount { get; set; }

    /// <summary>
    /// Target body/size group code (<c>regular</c>, <c>plus</c>, <c>midsize</c>, <c>curvy</c>, <c>petite</c>, <c>tall</c>).
    /// </summary>
    public string? BodySizeCode { get; set; }

    /// <summary>
    /// Fashion style codes JSON array (e.g. <c>["elegant","casual"]</c>). Whitelist in <c>ShopStyleCodes</c>.
    /// </summary>
    public string? StyleCodes { get; set; }

    /// <summary>
    /// LLC company name.
    /// </summary>
    public string? LlcName { get; set; }

    /// <summary>
    /// Two-factor authentication secret.
    /// </summary>
    public string? TwoFactorAuth { get; set; }

    /// <summary>
    /// Display order (lower = earlier in lists). Managed by admin drag-and-drop.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// TikTok seller shop code (e.g. USLC4KELN2).
    /// </summary>
    public string? ShopCode { get; set; }

    /// <summary>
    /// TikTok shop_status numeric. Ops Status is derived from seller.status / shop_status on TTS sync.
    /// </summary>
    public int? TikTokShopStatus { get; set; }

    /// <summary>
    /// Shop timezone (e.g. America/Los_Angeles).
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// TikTok shop create time (UTC), from global_seller.create_time.
    /// </summary>
    public DateTime? TikTokCreatedAt { get; set; }

    /// <summary>
    /// Number of verified documents on TikTok.
    /// </summary>
    public int? VerifiedDocuments { get; set; }

    /// <summary>
    /// Whether account email is verified on TikTok.
    /// </summary>
    public bool? EmailVerified { get; set; }

    /// <summary>
    /// Whether account phone is verified on TikTok.
    /// </summary>
    public bool? PhoneVerified { get; set; }

    /// <summary>
    /// TikTok seller_type: 1 Cross-border, 2 POP/Local.
    /// </summary>
    public int? SellerType { get; set; }

    /// <summary>
    /// TikTok business_type raw (0/1 individual, 2 solepro, 3 partner, 4 corp).
    /// </summary>
    public int? BusinessType { get; set; }

    /// <summary>
    /// Pay onboard enterprise.entity_type.
    /// </summary>
    public int? PayEntityType { get; set; }

    /// <summary>
    /// Company EIN / certificate number.
    /// </summary>
    public string? CompanyCertNum { get; set; }

    /// <summary>
    /// Certificate type from pay onboard.
    /// </summary>
    public int? CompanyCertType { get; set; }

    /// <summary>
    /// Passport / account display name.
    /// </summary>
    public string? AccountName { get; set; }

    /// <summary>
    /// TikTok passport is_blocked.
    /// </summary>
    public bool? IsBlocked { get; set; }

    /// <summary>
    /// TikTok passport is_blocking.
    /// </summary>
    public bool? IsBlocking { get; set; }

    /// <summary>
    /// TikTok publish_limit.published_num.
    /// </summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>
    /// TikTok publish_limit.total_limit.
    /// </summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>
    /// TikTok Health Center account health rating (<c>account/ahr/score</c> → <c>data.score</c>).
    /// </summary>
    public int? AhrScore { get; set; }

    /// <summary>
    /// Shop Performance Score (SPS) 0–5 from <c>experience-score/score-overview</c> → <c>overview.score</c>.
    /// </summary>
    public decimal? ShopPerformanceScore { get; set; }

    /// <summary>
    /// SPS metric group <c>020100</c> — Product satisfaction (0–5).
    /// </summary>
    public decimal? ProductSatisfactionScore { get; set; }

    /// <summary>
    /// SPS metric group <c>020200</c> — Fulfillment and logistics (0–5).
    /// </summary>
    public decimal? FulfillmentScore { get; set; }

    /// <summary>
    /// SPS metric group <c>020300</c> — Customer service (0–5).
    /// </summary>
    public decimal? CustomerServiceScore { get; set; }

    /// <summary>
    /// Last successful TikTok shop score sync from extension (UTC).
    /// </summary>
    public DateTime? TikTokShopScoreSyncedAt { get; set; }

    /// <summary>
    /// Last successful TikTok profile sync from extension (UTC).
    /// </summary>
    public DateTime? TikTokProfileSyncedAt { get; set; }

    /// <summary>
    /// Last successful Product opportunities (trending keywords) snapshot from extension (UTC).
    /// </summary>
    public DateTime? ProductOpportunitiesSyncedAt { get; set; }

    /// <summary>
    /// Company address JSON (camelCase).
    /// </summary>
    public string? CompanyAddress { get; set; }

    /// <summary>
    /// Owner / relate person JSON (camelCase).
    /// </summary>
    public string? OwnerPerson { get; set; }

    /// <summary>
    /// Logistics / shipping services JSON array (camelCase).
    /// </summary>
    public string? LogisticsServices { get; set; }

    /// <summary>
    /// TikTok product categories JSON array (camelCase).
    /// </summary>
    public string? ProductCategories { get; set; }

    /// <summary>
    /// Masked settlement bank account number from TikTok settlement/account/get.
    /// Separate from <see cref="NewBankNumber"/> (receiving-bank card).
    /// </summary>
    public string? SettlementBankAccountNumber { get; set; }

    /// <summary>
    /// Settlement payment method (e.g. Bank account).
    /// </summary>
    public string? SettlementPaymentMethod { get; set; }

    /// <summary>
    /// Settlement beneficiary name (often masked).
    /// </summary>
    public string? SettlementBeneficiaryName { get; set; }

    /// <summary>
    /// Settlement account type (e.g. CHECKING).
    /// </summary>
    public string? SettlementAccountType { get; set; }

    /// <summary>
    /// Settlement account status from TikTok.
    /// </summary>
    public int? SettlementAccountStatus { get; set; }

    /// <summary>
    /// TikTok Finance On hold total (<c>to_settle_amount_stat.amount.amount</c>).
    /// </summary>
    public decimal? OnHoldAmount { get; set; }

    /// <summary>
    /// On hold — waiting for package delivery (<c>unsettled_statement_reasons_in_transit_new</c>).
    /// </summary>
    public decimal? OnHoldInTransitAmount { get; set; }

    /// <summary>
    /// On hold — delivered, awaiting settlement (<c>unsettled_statement_reasons_settlement_in_progress_new</c>).
    /// </summary>
    public decimal? OnHoldAwaitingSettlementAmount { get; set; }

    /// <summary>
    /// On hold — reserve withheld (<c>finance_payments_table_type_reserve</c>).
    /// </summary>
    public decimal? OnHoldReserveAmount { get; set; }

    /// <summary>Per-shop auto publish toggle (requires system setting ON).</summary>
    public bool AutoPublishEnabled { get; set; }

    /// <summary>Seconds between auto publishes for this shop (null = system default).</summary>
    public int? AutoPublishIntervalSeconds { get; set; }

    /// <summary>Minimum draft age in minutes before auto publish may pick (null = system default).</summary>
    public int? AutoPublishMinDraftAgeMinutes { get; set; }

    /// <summary>UTC timestamp of last auto publish attempt for interval enforcement.</summary>
    public DateTime? LastAutoPublishAt { get; set; }

    /// <summary>
    /// When set and in the future, auto publish skips this shop (e.g. Day_Limit until next VN midnight).
    /// </summary>
    public DateTime? AutoPublishBlockedUntil { get; set; }

    /// <summary>Short reason for <see cref="AutoPublishBlockedUntil"/> (e.g. <c>day_limit</c>).</summary>
    public string? AutoPublishBlockReason { get; set; }

    /// <summary>
    /// Navigation to owner.
    /// </summary>
    public ApplicationUser Owner { get; set; } = null!;

    /// <summary>
    /// Navigation to platform.
    /// </summary>
    public Platform Platform { get; set; } = null!;

    /// <summary>
    /// Navigation to receiving bank account.
    /// </summary>
    public ReceivingBankAccount? ReceivingBankAccount { get; set; }

}
