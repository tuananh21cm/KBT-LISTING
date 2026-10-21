namespace Shared.Constants;

/// <summary>Status values for <see cref="Domain.Entities.ListingAutoPublishRun"/>.</summary>
public static class ListingAutoPublishRunStatuses
{
    public const string Running = "running";
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string Cancelled = "cancelled";
}

/// <summary>How a listing was published to 4Seller.</summary>
public static class ListingPublishSources
{
    public const string Manual = "manual";
    public const string Auto = "auto";
}
