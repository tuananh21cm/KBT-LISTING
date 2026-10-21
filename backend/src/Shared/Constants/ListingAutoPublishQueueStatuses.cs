namespace Shared.Constants;

/// <summary>Queue/progress status for draft listings in auto publish (list UI).</summary>
public static class ListingAutoPublishQueueStatuses
{
    /// <summary>In FIFO line behind the shop head — not next to publish.</summary>
    public const string Queue = "queue";

    /// <summary>Next eligible draft for the shop (FIFO head).</summary>
    public const string Waiting = "waiting";

    public const string Processing = "processing";
    public const string Failed = "failed";
}
