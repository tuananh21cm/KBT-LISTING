namespace Shared.Constants;

/// <summary>Settings keys for background product-discount activity sync from 4Seller.</summary>
public static class ProductDiscountSyncSettingKeys
{
    /// <summary>Enable background Product Discount sync.</summary>
    public const string Enabled = "fourseller.product_discount_sync.enabled";

    /// <summary>
    /// Hours to wait after a completed automatic PD sync before the next run (minimum 1).
    /// </summary>
    public const string IntervalHours = "fourseller.product_discount_sync.interval_hours";

    /// <summary>Legacy minutes key — no longer read by the background job.</summary>
    public const string IntervalMinutes = "fourseller.product_discount_sync.interval_minutes";
}

/// <summary>Settings keys for background Flash Deal activity sync from 4Seller.</summary>
public static class FlashDealSyncSettingKeys
{
    /// <summary>Enable background Flash Deal sync.</summary>
    public const string Enabled = "fourseller.flash_deal_sync.enabled";

    /// <summary>
    /// Hours to wait after a completed automatic Flash Deal sync before the next run (minimum 1).
    /// </summary>
    public const string IntervalHours = "fourseller.flash_deal_sync.interval_hours";
}

/// <summary>4Seller promotion activity discountType values we care about.</summary>
public static class PromotionDiscountTypes
{
    /// <summary>TikTok Flash Deal / flash sale activities.</summary>
    public const string FlashSale = "FLASHSALE";
}

/// <summary>Lifecycle of a manual product-discount sync job.</summary>
public static class ProductDiscountSyncJobStates
{
    /// <summary>Still pulling; keep polling.</summary>
    public const string Running = "running";

    /// <summary>Finished (individual sellers may have failed).</summary>
    public const string Completed = "completed";

    /// <summary>The run itself threw.</summary>
    public const string Failed = "failed";

    /// <summary>True once polling can stop.</summary>
    public static bool IsTerminal(string? state) =>
        string.Equals(state, Completed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, Failed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Per-seller progress inside a product-discount sync job.</summary>
public static class ProductDiscountSyncSellerStatuses
{
    /// <summary>Queued behind concurrency limit.</summary>
    public const string Pending = "pending";

    /// <summary>Currently pulling from 4Seller.</summary>
    public const string Running = "running";

    /// <summary>Finished successfully.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Failed; other sellers continue.</summary>
    public const string Failed = "failed";
}
