namespace Shared.Constants;

/// <summary>Sync type values for <see cref="Domain.Entities.TikTokShopSyncLog"/>.</summary>
public static class TikTokShopSyncTypes
{
    /// <summary>PUT /shops/tiktok-profile — shop info.</summary>
    public const string Profile = "profile";

    /// <summary>POST /shops/tiktok-payouts/sync — finance ledger.</summary>
    public const string Payout = "payout";

    /// <summary>POST /shops/tiktok-opportunities/sync — trending keywords.</summary>
    public const string Opportunity = "opportunity";

    /// <summary>POST /shops/tiktok-listing-analytics/sync — Compass product analysis.</summary>
    public const string Analytics = "analytics";

    /// <summary>POST /shops/tiktok-health-records/sync — Growth Center violations/warnings.</summary>
    public const string HealthCenter = "health_center";

    /// <summary>POST /shops/tiktok-shop-scores/sync — AHR + Shop Performance Score.</summary>
    public const string ShopScore = "shop_score";
}
