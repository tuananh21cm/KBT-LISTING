namespace Application.DTOs.Shop;

/// <summary>
/// Upsert TikTok seller-us profile onto an existing shop (match by externalShopId = sellerId).
/// Does not create shops.
/// </summary>
public class SyncTikTokShopProfileDto
{
    /// <summary>Run id from extension unified Sync (optional).</summary>
    public string? RunId { get; set; }

    /// <summary>TikTok seller id — matches Shop.ExternalShopId.</summary>
    public string SellerId { get; set; } = string.Empty;

    /// <summary>Display shop name.</summary>
    public string? ShopName { get; set; }

    /// <summary>Shop code (e.g. USLC4KELN2).</summary>
    public string? ShopCode { get; set; }

    /// <summary>Region code (US, …).</summary>
    public string? Region { get; set; }

    /// <summary>Company / LLC name.</summary>
    public string? CompanyName { get; set; }

    /// <summary>Logo URL.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>TikTok shop_status.</summary>
    public int? ShopStatus { get; set; }

    /// <summary>
    /// TikTok <c>seller.status</c> (account): 2 active, 3 deactivated.
    /// Used with <see cref="IdentityVideoVerificationLock"/> to set ops <c>Shop.Status</c>.
    /// </summary>
    public int? SellerStatus { get; set; }

    /// <summary>
    /// Shop-level 365-day listing lock from TTS products/list
    /// (<c>post_onboarding_identity_video_verification_reason</c>).
    /// When true, ops status is <c>Suspended</c> even if TTS reports Active.
    /// False / omitted does not unsuspend.
    /// </summary>
    public bool? IdentityVideoVerificationLock { get; set; }

    /// <summary>Timezone name.</summary>
    public string? Timezone { get; set; }

    /// <summary>Create time in Unix ms.</summary>
    public long? CreateTimeMs { get; set; }

    /// <summary>Phone verified flag.</summary>
    public bool? PhoneVerified { get; set; }

    /// <summary>Email verified flag.</summary>
    public bool? EmailVerified { get; set; }

    /// <summary>Verified documents count.</summary>
    public int? VerifiedDocuments { get; set; }

    /// <summary>seller_type (1 CB / 2 POP).</summary>
    public int? SellerType { get; set; }

    /// <summary>business_type raw.</summary>
    public int? BusinessType { get; set; }

    /// <summary>Inferred Per / Llc (extension casing).</summary>
    public string? ShopType { get; set; }

    /// <summary>Product categories.</summary>
    public List<ShopProductCategoryDto>? ProductCategories { get; set; }

    /// <summary>Shipping / logistics services.</summary>
    public List<ShopLogisticsServiceDto>? LogisticsServices { get; set; }

    /// <summary>Published listing count.</summary>
    public int? PublishLimitPublished { get; set; }

    /// <summary>Total listing limit.</summary>
    public int? PublishLimitTotal { get; set; }

    /// <summary>
    /// Nested publish_limit from extension payload (<c>{ publishedNum, totalLimit }</c>).
    /// Preferred when flat publish fields are omitted.
    /// </summary>
    public SyncTikTokPublishLimitDto? PublishLimit { get; set; }

    /// <summary>
    /// Diagnostics for the Info step: where publish_limit came from (<c>page-harvest</c> /
    /// <c>bridge</c> / <c>isolated …</c>) or why it is missing, plus how many payloads were
    /// harvested from the page's own requests (<c>harvest 6/8</c>). Stored on the Profile sync
    /// log so ops can tell a blocked TikTok product gateway apart from a shop with no limit yet.
    /// </summary>
    public string? SyncNote { get; set; }

    /// <summary>Company address.</summary>
    public ShopAddressDto? CompanyAddress { get; set; }

    /// <summary>EIN / cert number.</summary>
    public string? CompanyCertNum { get; set; }

    /// <summary>Cert type.</summary>
    public int? CompanyCertType { get; set; }

    /// <summary>Pay entity type.</summary>
    public int? PayEntityType { get; set; }

    /// <summary>Owner person.</summary>
    public ShopOwnerPersonDto? Owner { get; set; }

    /// <summary>Account email.</summary>
    public string? AccountEmail { get; set; }

    /// <summary>Account mobile.</summary>
    public string? AccountMobile { get; set; }

    /// <summary>Account / passport name.</summary>
    public string? AccountName { get; set; }

    /// <summary>is_blocked.</summary>
    public bool? IsBlocked { get; set; }

    /// <summary>is_blocking.</summary>
    public bool? IsBlocking { get; set; }

    /// <summary>Masked settlement bank account number (TTS). Alias of <see cref="SettlementBankAccountNumber"/>.</summary>
    public string? BankAccountNumber { get; set; }

    /// <summary>Masked settlement bank (preferred explicit name from newer clients).</summary>
    public string? SettlementBankAccountNumber { get; set; }

    /// <summary>Settlement payment method.</summary>
    public string? SettlementPaymentMethod { get; set; }

    /// <summary>Settlement beneficiary name.</summary>
    public string? SettlementBeneficiaryName { get; set; }

    /// <summary>Settlement account type (CHECKING, …).</summary>
    public string? SettlementAccountType { get; set; }

    /// <summary>Settlement account status.</summary>
    public int? SettlementAccountStatus { get; set; }

    /// <summary>On hold total (TTS <c>to_settle_amount_stat</c>).</summary>
    public decimal? OnHoldAmount { get; set; }

    /// <summary>On hold — waiting for package delivery.</summary>
    public decimal? OnHoldInTransitAmount { get; set; }

    /// <summary>On hold — delivered, awaiting settlement.</summary>
    public decimal? OnHoldAwaitingSettlementAmount { get; set; }

    /// <summary>On hold — reserve withheld.</summary>
    public decimal? OnHoldReserveAmount { get; set; }

    /// <summary>
    /// Settlement period in days (TTS <c>bill_finish_period_in_days</c>) → <c>Shop.Deli</c>.
    /// </summary>
    public int? Deli { get; set; }
}

/// <summary>
/// Nested publish limit from TikTokShopInfo.
/// </summary>
public class SyncTikTokPublishLimitDto
{
    /// <summary>Total listing limit.</summary>
    public int? TotalLimit { get; set; }

    /// <summary>Published listing count.</summary>
    public int? PublishedNum { get; set; }
}
