namespace Domain.Entities;

using Shared.Constants;

/// <summary>
/// One auto-publish attempt for a draft listing — timeline log for prep + publish steps.
/// </summary>
public class ListingAutoPublishRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ListingId { get; set; }

    public Guid ShopId { get; set; }

    public Guid OwnerId { get; set; }

  /// <summary><see cref="ListingAutoPublishRunStatuses"/>.</summary>
    public string Status { get; set; } = ListingAutoPublishRunStatuses.Running;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public int DurationMs { get; set; }

    /// <summary>JSON array of step results for timeline UI.</summary>
    public string? StepsJson { get; set; }

    public string? FailedStepCode { get; set; }

    public string? ErrorMessage { get; set; }

    public Listing Listing { get; set; } = null!;

    public Shop Shop { get; set; } = null!;
}
