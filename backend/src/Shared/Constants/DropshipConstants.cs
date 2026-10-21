namespace Shared.Constants;

/// <summary>
/// E-commerce platform identifiers.
/// </summary>
public static class ShopPlatforms
{
    /// <summary>TikTok Shop US.</summary>
    public const string TikTokShopUs = "TikTokShopUS";
}

/// <summary>
/// Shop status values.
/// </summary>
public static class ShopStatuses
{
    public const string Active = "Active";
    public const string Deactivated = "Deactivated";
    public const string Suspended = "Suspended";

    public static readonly string[] All = [Active, Deactivated, Suspended];
}

/// <summary>
/// Shop ownership type (Per = cá nhân, Llc = công ty).
/// </summary>
public static class ShopTypes
{
    /// <summary>Personal / sole-proprietor shop.</summary>
    public const string Per = "Per";

    /// <summary>LLC / company shop.</summary>
    public const string Llc = "Llc";

    public static readonly string[] All = [Per, Llc];
}

/// <summary>
/// Product status values.
/// </summary>
public static class ProductStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Archived = "Archived";

    public static readonly string[] All = [Draft, Active, Archived];
}

/// <summary>
/// Shop product listing status values.
/// </summary>
public static class ListingStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Removed = "Removed";
    public const string Syncing = "Syncing";

    public static readonly string[] All = [Draft, Published, Removed, Syncing];
}

/// <summary>
/// Synced listing page buckets (admin Listings mirror).
/// </summary>
public static class ListingPageTypes
{
    public const string Draft = "draft";
    public const string Published = "published";

    public static readonly string[] All = [Draft, Published];
}

/// <summary>
/// Local-only origin of a listing add (extension Search vs PDP thủ công).
/// Null on the listing = unknown / legacy (không đoán «thêm tay»).
/// </summary>
public static class ListingAddedFrom
{
    /// <summary>Added from extension Search results (bulk or click-then-add).</summary>
    public const string Recommended = "recommended";

    /// <summary>Added from a source PDP that was not in the Search list.</summary>
    public const string Manual = "manual";

    /// <summary>Known origin values.</summary>
    public static readonly string[] All = [Recommended, Manual];
}

/// <summary>
/// 4Seller listing site statuses used for page sync / Published tabs.
/// </summary>
public static class ListingSiteStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Inactive = "inactive";
    public const string Removed = "removed";
    public const string Incomplete = "incomplete";
    public const string Suspended = "suspended";
    public const string Private = "private";
    public const string Pending = "pending";
    public const string Reject = "reject";
    public const string Unsaleable = "unsaleable";

    /// <summary>Statuses pulled when syncing without an explicit filter.</summary>
    public static readonly string[] DefaultSyncStatuses =
    [
        Draft, Active, Inactive, Suspended, Removed, Incomplete, Private, Pending, Reject, Unsaleable
    ];

    /// <summary>Published-tab site statuses (excludes draft).</summary>
    public static readonly string[] PublishedStatuses =
    [
        Active, Inactive, Suspended, Removed, Incomplete, Private, Pending, Reject, Unsaleable
    ];

    /// <summary>
    /// Maps local/UI site status → 4Seller <c>/api/listing/{platform}/page</c> <c>status</c> query.
    /// 4Seller uses <c>remove</c> (not <c>removed</c>).
    /// </summary>
    public static string ToFourSellerPageStatus(string? siteStatus)
    {
        var s = (siteStatus ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "removed" => "remove",
            _ => string.IsNullOrEmpty(s) ? Draft : s
        };
    }

    /// <summary>
    /// Normalizes 4Seller erp / page status strings to local <see cref="ListingSiteStatuses"/> values.
    /// </summary>
    public static string NormalizeSiteStatus(string? raw)
    {
        var s = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "remove" => Removed,
            "removed" => Removed,
            "reviewing" => Pending,
            "under_review" => Pending,
            "underreview" => Pending,
            "failure" => Reject,
            "failed" => Reject,
            "rejected" => Reject,
            _ => s
        };
    }
}

/// <summary>
/// TikTok <c>productStatus</c> numeric codes from 4Seller listing page rows.
/// </summary>
public static class FourSellerProductStatuses
{
    public const int Draft = 1;
    /// <summary>Posted to shop, waiting TikTok review (UI: Pending / Reviewing).</summary>
    public const int Reviewing = 2;
    public const int Failure = 3;
    public const int Active = 4;
    public const int Inactive = 6;
    public const int Remove = 8;
}

/// <summary>
/// 4Seller publish status values (detailed).
/// </summary>
public static class PublishStatuses
{
    public const string Draft = "Draft";
    /// <summary>4Seller in-flight publish/update (<c>publishStatus=publishing</c>).</summary>
    public const string Publishing = "Publishing";
    public const string UnderReview = "UnderReview";
    public const string Published = "Published";
    public const string Failed = "Failed";
    public const string Deactivated = "Deactivated";

    public static readonly string[] All = [Draft, Publishing, UnderReview, Published, Failed, Deactivated];

    /// <summary>
    /// Lowercase in-flight values for EF Core <c>Contains(status.ToLower())</c>.
    /// Do not call <see cref="IsInFlight"/> inside <c>IQueryable</c> — EF cannot translate it.
    /// </summary>
    public static readonly string[] InFlightLowercase = ["publishing", "underreview"];

    /// <summary>Lowercase failed publish — exclude from auto-publish pick (EF Contains).</summary>
    public const string FailedLowercase = "failed";

    /// <summary>Maps 4Seller publish status strings to canonical <see cref="PublishStatuses"/> values.</summary>
    public static string? Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var trimmed = status.Trim();
        foreach (var known in All)
        {
            if (string.Equals(trimmed, known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Whether <paramref name="publishStatus"/> blocks auto-publish pick (in-flight or Failed).
    /// </summary>
    public static bool BlocksAutoPublishPick(string? publishStatus)
    {
        var normalized = Normalize(publishStatus);
        return normalized is Publishing or UnderReview or Failed;
    }

    /// <summary>Whether the listing is awaiting TikTok after a publish/update push.</summary>
    public static bool IsInFlight(string? status)
    {
        var normalized = Normalize(status);
        return normalized is Publishing or UnderReview;
    }
}

/// <summary>
/// Internal listing sync status values.
/// </summary>
public static class SyncStatuses
{
    public const string Pending = "Pending";
    public const string Synced = "Synced";
    public const string Error = "Error";

    public static readonly string[] All = [Pending, Synced, Error];
}

/// <summary>
/// Listing media type values.
/// </summary>
public static class ListingMediaTypes
{
    public const string Main = "Main";
    public const string Detail = "Detail";
    public const string Video = "Video";
    public const string SizeChart = "SizeChart";
    public const string Certification = "Certification";

    public static readonly string[] All = [Main, Detail, Video, SizeChart, Certification];
}

/// <summary>
/// Listing certification type values.
/// </summary>
public static class ListingCertTypes
{
    public const string Manufacturer = "Manufacturer";
    public const string ResponsiblePerson = "ResponsiblePerson";
    public const string ProductCert = "ProductCert";
    public const string Compliance = "Compliance";

    public static readonly string[] All = [Manufacturer, ResponsiblePerson, ProductCert, Compliance];
}

/// <summary>
/// TikTok listing product type values.
/// </summary>
public static class ListingProductTypes
{
    public const string SingleSku = "SingleSku";
    public const string MultiSku = "MultiSku";

    public static readonly string[] All = [SingleSku, MultiSku];
}

/// <summary>
/// Linked entity types for user files.
/// </summary>
public static class FileLinkedEntityTypes
{
    public const string Product = "Product";
    public const string Shop = "Shop";
}

/// <summary>
/// Scrape batch status values.
/// </summary>
public static class ScrapeBatchStatuses
{
    public const string Pending = "Pending";
    /// <summary>Pending after one mid-poll re-submit to 4Seller.</summary>
    public const string Retrying = "Retrying";
    public const string Ready = "Ready";
    public const string Failed = "Failed";

    public static readonly string[] All = [Pending, Retrying, Ready, Failed];
}
