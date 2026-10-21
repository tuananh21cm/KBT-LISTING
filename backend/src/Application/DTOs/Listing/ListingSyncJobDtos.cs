using Shared.Constants;

namespace Application.DTOs.Listing;

/// <summary>
/// Progress of one seller inside a listing sync job. Reported as the run advances so the UI can
/// show accounts finishing one by one instead of waiting for the slowest.
/// </summary>
public class ListingSyncSellerProgressDto
{
    /// <summary>Owner user id.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Display name.</summary>
    public string? OwnerName { get; set; }

    /// <summary>One of <see cref="ListingSyncSellerStatuses"/>.</summary>
    public string Status { get; set; } = ListingSyncSellerStatuses.Pending;

    /// <summary>Rows inserted for this seller.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated for this seller.</summary>
    public int Updated { get; set; }

    /// <summary>Wall time spent on this seller once it left <c>pending</c>.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>Failure / skip reason when not succeeded.</summary>
    public string? Message { get; set; }
}

/// <summary>Snapshot of a manual listing sync job, returned by start and by polling.</summary>
public class ListingSyncJobStatusDto
{
    /// <summary>Poll <c>GET .../sync/status/{jobId}</c> with this id.</summary>
    public Guid JobId { get; set; }

    /// <summary>One of <see cref="ListingSyncJobStates"/>.</summary>
    public string State { get; set; } = ListingSyncJobStates.Running;

    /// <summary>When the job was queued.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>When the job reached a terminal state.</summary>
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>Sellers in scope. 0 until the owner list has been resolved.</summary>
    public int TotalSellers { get; set; }

    /// <summary>Sellers that reached a terminal per-seller status.</summary>
    public int CompletedSellers { get; set; }

    /// <summary>Sellers synced successfully.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>Rows inserted so far.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated so far.</summary>
    public int Updated { get; set; }

    /// <summary>In-flight rows updated via listingId reconcile.</summary>
    public int ReconciledUpdated { get; set; }

    /// <summary>Local mirrors removed (gone from 4Seller) during Manual reconcile.</summary>
    public int ReconciledRemoved { get; set; }

    /// <summary>Local in-flight/pending remaining after the job (sum across sellers).</summary>
    public int StillInFlight { get; set; }

    /// <summary>Single-seller run that could not take the lease.</summary>
    public bool SkippedBusy { get; set; }

    /// <summary>Human-readable summary once finished.</summary>
    public string? Message { get; set; }

    /// <summary>Set when <see cref="State"/> is <c>failed</c>.</summary>
    public string? Error { get; set; }

    /// <summary>Per-seller progress.</summary>
    public List<ListingSyncSellerProgressDto> Sellers { get; set; } = [];
}
