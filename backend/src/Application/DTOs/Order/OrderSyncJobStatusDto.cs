using Shared.Constants;

namespace Application.DTOs.Order;

/// <summary>
/// Snapshot of a manual order sync job, returned by start and by polling.
/// Mirrors listing sync: the HTTP start request returns immediately so Cloudflare cannot 524.
/// </summary>
public class OrderSyncJobStatusDto
{
    /// <summary>Poll <c>GET .../sync/status/{jobId}</c> with this id.</summary>
    public Guid JobId { get; set; }

    /// <summary>One of <see cref="OrderSyncJobStates"/>.</summary>
    public string State { get; set; } = OrderSyncJobStates.Running;

    /// <summary>When the job was queued.</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>When the job reached a terminal state.</summary>
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>Sellers synced successfully.</summary>
    public int SyncedSellers { get; set; }

    /// <summary>Rows inserted.</summary>
    public int Inserted { get; set; }

    /// <summary>Rows updated.</summary>
    public int Updated { get; set; }

    /// <summary>Per-seller failures (cookie expired, API error, gate busy, …).</summary>
    public List<OrderSyncSellerFailureDto> FailedSellers { get; set; } = [];

    /// <summary>Human-readable summary once finished.</summary>
    public string? Message { get; set; }

    /// <summary>Set when <see cref="State"/> is <c>failed</c>.</summary>
    public string? Error { get; set; }
}
