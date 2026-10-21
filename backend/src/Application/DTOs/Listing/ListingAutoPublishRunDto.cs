namespace Application.DTOs.Listing;

/// <summary>One auto publish run for timeline UI.</summary>
public class ListingAutoPublishRunDto
{
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }

    public Guid ShopId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int DurationMs { get; set; }

    public string? FailedStepCode { get; set; }

    public string? ErrorMessage { get; set; }

    public List<ListingAutoPublishStepDto> Steps { get; set; } = [];
}

/// <summary>One pipeline step in a run.</summary>
public class ListingAutoPublishStepDto
{
    public string Code { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool Success { get; set; }

    public bool Skipped { get; set; }

    /// <summary>True while the step is executing; cleared when the step finishes or fails.</summary>
    public bool InProgress { get; set; }

    public string? Error { get; set; }

    public int DurationMs { get; set; }

    public List<ListingAutoPublishSubStepDto>? SubSteps { get; set; }
}

/// <summary>Sub-step (e.g. load_from_db inside load_full).</summary>
public class ListingAutoPublishSubStepDto
{
    public string Code { get; set; } = string.Empty;

    public bool Success { get; set; }

    public bool Skipped { get; set; }

    public string? Error { get; set; }

    /// <summary>Human-readable detail of what the sub-step decided (audit trail).</summary>
    public string? Note { get; set; }
}
