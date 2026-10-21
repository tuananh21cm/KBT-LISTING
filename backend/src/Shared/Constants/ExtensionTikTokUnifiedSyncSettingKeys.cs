namespace Shared.Constants;

/// <summary>
/// Setting keys for which Sync All / Sync Fast steps the tiktok-shop-extension may run
/// (managed via admin Settings — separate from Auto Sync <c>extension.tiktok_*_sync.enabled</c>).
/// </summary>
public static class ExtensionTikTokUnifiedSyncSettingKeys
{
    public const string StepInfo = "extension.tiktok_unified_sync.step_info";
    public const string StepFinance = "extension.tiktok_unified_sync.step_finance";
    public const string StepKeywords = "extension.tiktok_unified_sync.step_keywords";
    public const string StepAnalytics = "extension.tiktok_unified_sync.step_analytics";
    public const string StepHealthCenter = "extension.tiktok_unified_sync.step_health_center";
    public const string StepShopScore = "extension.tiktok_unified_sync.step_shop_score";

    /// <summary>Default when setting row missing — keep Sync All fully on.</summary>
    public const bool DefaultStepEnabled = true;
}
