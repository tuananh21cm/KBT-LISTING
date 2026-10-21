namespace Shared.Constants;

/// <summary>
/// Setting keys for background auto publish of draft listings (admin Settings).
/// </summary>
public static class ListingAutoPublishSettingKeys
{
    public const string Enabled = "listing.auto_publish.enabled";
    public const string SchedulerIntervalMinutes = "listing.auto_publish.scheduler_interval_minutes";
    public const string MaxConcurrentPublishes = "listing.auto_publish.max_concurrent_publishes";
    public const string DefaultIntervalSeconds = "listing.auto_publish.default_interval_seconds";
    public const string DefaultMinDraftAgeMinutes = "listing.auto_publish.default_min_draft_age_minutes";
    public const string LogRetentionDays = "listing.auto_publish.log_retention_days";
    public const string CategoryVerifyEnabled = "listing.auto_publish.category_verify_enabled";
    public const string CategoryRiskyAncestorIds = "listing.auto_publish.category_risky_ancestor_ids";
    public const string CategoryRiskyNameKeywords = "listing.auto_publish.category_risky_name_keywords";
    public const string CategoryRiskyTitleKeywords = "listing.auto_publish.category_risky_title_keywords";
    public const string CategoryBlockedRemap = "listing.auto_publish.category_blocked_remap";

    /// <summary>Minimum allowed min-draft-age for system default and per-shop override (minutes).</summary>
    public const int MinDraftAgeMinutes = 5;

    public const string DefaultEnabled = "false";
    public const string DefaultSchedulerIntervalMinutes = "5";
    public const string DefaultMaxConcurrentPublishes = "10";
    public const string DefaultIntervalSecondsValue = "120";
    public const string DefaultMinDraftAgeMinutesValue = "5";
    public const string DefaultLogRetentionDays = "30";
    public const string DefaultCategoryVerifyEnabled = "true";

    /// <summary>
    /// Costumes &amp; Accessories (843656) — its leaves carry costume-only attribute enums that
    /// TikTok rejects for ordinary fashion items.
    /// </summary>
    public const string DefaultCategoryRiskyAncestorIds = "843656";

    public const string DefaultCategoryRiskyNameKeywords = "costume,cosplay";

    /// <summary>
    /// Only words that name the product itself. Occasion words alone ("halloween", "carnival") are
    /// marketing filler in scraped titles — "Corset Tank Top … Suitable for Music Festivals, Halloween
    /// and Other Occasions" is an ordinary top, and 4Seller AI classifies it as one.
    /// </summary>
    public const string DefaultCategoryRiskyTitleKeywords = "costume,cosplay";

    /// <summary>
    /// <c>blockedId:replacementId</c> pairs (comma-separated). Measured 2026-09-26: leaf
    /// <c>903824</c> (Costume Tops &amp; Corsets) returns TikTok
    /// <c>Cannot use custom value names … attribute ID 100401</c> for some shops even when the
    /// payload carries a numeric <c>valueId</c> that 4Seller lists in the schema and that other
    /// shops publish with. Sibling <c>903952</c> (Costume Sets) accepts the same values.
    /// </summary>
    public const string DefaultCategoryBlockedRemap = "903824:903952";

    /// <summary>Effective min draft age for a shop (override or system default), never below <see cref="MinDraftAgeMinutes"/>.</summary>
    public static int ResolveMinDraftAgeMinutes(int? shopOverrideMinutes, int systemDefaultMinutes) =>
        Math.Max(MinDraftAgeMinutes, shopOverrideMinutes ?? systemDefaultMinutes);
}
