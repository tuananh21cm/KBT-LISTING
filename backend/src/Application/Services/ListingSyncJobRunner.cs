using System.Collections.Concurrent;
using Application.DTOs.Listing;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// In-memory listing sync jobs (single API process), mirroring how order sync keeps its runtime
/// state. A manual sync can take minutes, so it runs detached from the request that started it and
/// the UI polls <see cref="GetStatus"/> instead of holding a connection open.
/// </summary>
public sealed class ListingSyncJobRunner : IListingSyncJobRunner
{
    /// <summary>Finished jobs stay pollable this long so a slow client still sees the result.</summary>
    private static readonly TimeSpan CompletedJobTtl = TimeSpan.FromMinutes(30);

    /// <summary>Safety valve: a job that never reports terminal state is dropped eventually.</summary>
    private static readonly TimeSpan AbandonedJobTtl = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListingSyncJobRunner> _logger;

    /// <summary>Initializes the runner.</summary>
    public ListingSyncJobRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<ListingSyncJobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public ListingSyncJobStatusDto Start(ListingSyncRequest request, Guid? startedByUserId)
    {
        PruneExpired();

        var job = new JobState(Guid.NewGuid(), startedByUserId);
        _jobs[job.JobId] = job;

        // Detached on purpose: the caller's request completes immediately, and a browser timeout or
        // navigation must not cancel a run that is halfway through upserting.
        _ = Task.Run(() => RunAsync(job, request));

        return job.Snapshot();
    }

    /// <inheritdoc />
    public ListingSyncJobStatusDto? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job.Snapshot() : null;

    /// <inheritdoc />
    public Guid? GetStartedByUserId(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job.StartedByUserId : null;

    private async Task RunAsync(JobState job, ListingSyncRequest request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IListingSyncService>();
            var progress = new SellerProgressSink(job.ApplySellerProgress);

            var result = await syncService.SyncAsync(request, progress, CancellationToken.None);
            job.Complete(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Listing sync job {JobId} failed", job.JobId);
            job.Fail(ex);
        }
    }

    private void PruneExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, job) in _jobs)
        {
            if (job.IsExpired(now, CompletedJobTtl, AbandonedJobTtl))
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }

    /// <summary>
    /// Synchronous sink. <see cref="Progress{T}"/> would hand callbacks to the thread pool, which
    /// can deliver two updates for the same seller out of order and show a stale status.
    /// </summary>
    private sealed class SellerProgressSink : IProgress<ListingSyncSellerProgressDto>
    {
        private readonly Action<ListingSyncSellerProgressDto> _handler;

        public SellerProgressSink(Action<ListingSyncSellerProgressDto> handler) => _handler = handler;

        public void Report(ListingSyncSellerProgressDto value) => _handler(value);
    }

    private sealed class JobState
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, ListingSyncSellerProgressDto> _sellers = [];
        private readonly List<Guid> _sellerOrder = [];

        private string _state = ListingSyncJobStates.Running;
        private DateTime? _finishedAtUtc;
        private int _syncedSellers;
        private int _inserted;
        private int _updated;
        private int _reconciledUpdated;
        private int _reconciledRemoved;
        private int _stillInFlight;
        private bool _skippedBusy;
        private string? _message;
        private string? _error;

        public JobState(Guid jobId, Guid? startedByUserId)
        {
            JobId = jobId;
            StartedByUserId = startedByUserId;
            StartedAtUtc = DateTime.UtcNow;
        }

        public Guid JobId { get; }

        public Guid? StartedByUserId { get; }

        public DateTime StartedAtUtc { get; }

        public void ApplySellerProgress(ListingSyncSellerProgressDto update)
        {
            lock (_gate)
            {
                if (!_sellers.TryGetValue(update.OwnerUserId, out var existing))
                {
                    _sellers[update.OwnerUserId] = update;
                    _sellerOrder.Add(update.OwnerUserId);
                    return;
                }

                existing.OwnerName = update.OwnerName ?? existing.OwnerName;
                existing.Status = update.Status;
                existing.Inserted = update.Inserted;
                existing.Updated = update.Updated;
                existing.Message = update.Message;
                if (update.ElapsedMs > 0)
                {
                    existing.ElapsedMs = update.ElapsedMs;
                }
            }
        }

        public void Complete(ListingSyncResultDto result)
        {
            lock (_gate)
            {
                _state = ListingSyncJobStates.Completed;
                _finishedAtUtc = DateTime.UtcNow;
                _syncedSellers = result.SyncedSellers;
                _inserted = result.Inserted;
                _updated = result.Updated;
                _reconciledUpdated = result.ReconciledUpdated;
                _reconciledRemoved = result.ReconciledRemoved;
                _stillInFlight = result.StillInFlight;
                _skippedBusy = result.SkippedBusy;
                var reconcilePart =
                    result.ReconciledUpdated > 0 || result.ReconciledRemoved > 0 || result.StillInFlight > 0
                        ? $" reconcile cập nhật {result.ReconciledUpdated}, gỡ {result.ReconciledRemoved}, còn Reviewing {result.StillInFlight}."
                        : string.Empty;
                _message = result.FailedSellers.Count > 0
                    ? $"Đồng bộ xong: +{result.Inserted}, cập nhật {result.Updated}; {result.FailedSellers.Count} seller lỗi.{reconcilePart}"
                    : $"Đồng bộ xong: +{result.Inserted}, cập nhật {result.Updated}.{reconcilePart}";

                // Sellers filtered out before fan-out (no cookie, expired login) never reported
                // progress, so fold their failures in to keep the list complete.
                foreach (var failure in result.FailedSellers)
                {
                    if (_sellers.TryGetValue(failure.OwnerUserId, out var seller))
                    {
                        seller.Status = ListingSyncSellerStatuses.Failed;
                        seller.Message = failure.Message;
                        continue;
                    }

                    _sellers[failure.OwnerUserId] = new ListingSyncSellerProgressDto
                    {
                        OwnerUserId = failure.OwnerUserId,
                        OwnerName = failure.OwnerName,
                        Status = ListingSyncSellerStatuses.Failed,
                        Message = failure.Message
                    };
                    _sellerOrder.Add(failure.OwnerUserId);
                }
            }
        }

        public void Fail(Exception ex)
        {
            lock (_gate)
            {
                _state = ListingSyncJobStates.Failed;
                _finishedAtUtc = DateTime.UtcNow;
                _error = ex.Message;
                _message = "Đồng bộ thất bại.";
            }
        }

        public bool IsExpired(DateTime now, TimeSpan completedTtl, TimeSpan abandonedTtl)
        {
            lock (_gate)
            {
                if (_finishedAtUtc is { } finished)
                {
                    return now - finished > completedTtl;
                }

                return now - StartedAtUtc > abandonedTtl;
            }
        }

        public ListingSyncJobStatusDto Snapshot()
        {
            lock (_gate)
            {
                var sellers = _sellerOrder
                    .Select(id => _sellers[id])
                    .Select(s => new ListingSyncSellerProgressDto
                    {
                        OwnerUserId = s.OwnerUserId,
                        OwnerName = s.OwnerName,
                        Status = s.Status,
                        Inserted = s.Inserted,
                        Updated = s.Updated,
                        ElapsedMs = s.ElapsedMs,
                        Message = s.Message
                    })
                    .ToList();

                var completed = sellers.Count(s => IsTerminalSeller(s.Status));

                // While running, report live per-seller sums so the UI can count up instead of
                // jumping from zero to the final total.
                var running = _state == ListingSyncJobStates.Running;

                return new ListingSyncJobStatusDto
                {
                    JobId = JobId,
                    State = _state,
                    StartedAtUtc = StartedAtUtc,
                    FinishedAtUtc = _finishedAtUtc,
                    TotalSellers = sellers.Count,
                    CompletedSellers = completed,
                    SyncedSellers = running
                        ? sellers.Count(s => s.Status == ListingSyncSellerStatuses.Succeeded)
                        : _syncedSellers,
                    Inserted = running ? sellers.Sum(s => s.Inserted) : _inserted,
                    Updated = running ? sellers.Sum(s => s.Updated) : _updated,
                    ReconciledUpdated = _reconciledUpdated,
                    ReconciledRemoved = _reconciledRemoved,
                    StillInFlight = _stillInFlight,
                    SkippedBusy = _skippedBusy,
                    Message = _message,
                    Error = _error,
                    Sellers = sellers
                };
            }
        }

        private static bool IsTerminalSeller(string status) =>
            status is ListingSyncSellerStatuses.Succeeded
                or ListingSyncSellerStatuses.Failed
                or ListingSyncSellerStatuses.SkippedBusy;
    }
}
