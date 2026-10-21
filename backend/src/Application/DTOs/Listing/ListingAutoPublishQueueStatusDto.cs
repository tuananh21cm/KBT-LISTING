namespace Application.DTOs.Listing;

/// <summary>Lightweight auto publish queue badges for draft listing rows.</summary>
public class ListingAutoPublishQueueStatusBatchDto
{
    /// <summary>Per-listing queue status (only owned draft ids are returned).</summary>
    public List<ListingAutoPublishQueueStatusItemDto> Items { get; set; } = [];
}

/// <summary>Auto publish fields needed to refresh list badges without a full list reload.</summary>
public class ListingAutoPublishQueueStatusItemDto
{
    /// <summary>Local listing id.</summary>
    public Guid Id { get; set; }

    /// <summary>waiting | queue | processing | failed — null when no badge.</summary>
    public string? AutoPublishQueueStatus { get; set; }

    /// <summary>When queue status is waiting — UTC countdown target (shop interval).</summary>
    public DateTime? AutoPublishNextPickAt { get; set; }

    /// <summary>Latest auto publish run status.</summary>
    public string? LastAutoPublishRunStatus { get; set; }

    /// <summary>Scheduler skip flag.</summary>
    public bool AutoPublishSkipped { get; set; }

    /// <summary>Skip reason summary.</summary>
    public string? AutoPublishSkipReason { get; set; }

    /// <summary>Latest run log id.</summary>
    public Guid? LastAutoPublishRunId { get; set; }
}
