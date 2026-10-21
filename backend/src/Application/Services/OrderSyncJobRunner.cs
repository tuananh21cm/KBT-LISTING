using System.Collections.Concurrent;
using Application.DTOs.Order;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// In-memory order sync jobs (single API process). A manual sync can take minutes across many
/// 4Seller accounts, so it runs detached from the request that started it and the UI polls
/// <see cref="GetStatus"/> instead of holding a connection open (Cloudflare 524).
/// </summary>
public sealed class OrderSyncJobRunner : IOrderSyncJobRunner
{
    /// <summary>Finished jobs stay pollable this long so a slow client still sees the result.</summary>
    private static readonly TimeSpan CompletedJobTtl = TimeSpan.FromMinutes(30);

    /// <summary>Safety valve: a job that never reports terminal state is dropped eventually.</summary>
    private static readonly TimeSpan AbandonedJobTtl = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderSyncJobRunner> _logger;

    /// <summary>Initializes the runner.</summary>
    public OrderSyncJobRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<OrderSyncJobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public OrderSyncJobStatusDto Start(OrderSyncRequest request, Guid? startedByUserId)
    {
        PruneExpired();

        var job = new JobState(Guid.NewGuid(), startedByUserId);
        _jobs[job.JobId] = job;

        // Detached on purpose: the caller's request completes immediately, and a browser /
        // Cloudflare timeout must not cancel a run that is halfway through upserting.
        _ = Task.Run(() => RunAsync(job, request));

        return job.Snapshot();
    }

    /// <inheritdoc />
    public OrderSyncJobStatusDto? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job.Snapshot() : null;

    /// <inheritdoc />
    public Guid? GetStartedByUserId(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job.StartedByUserId : null;

    private async Task RunAsync(JobState job, OrderSyncRequest request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingProvider>();
            var syncService = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();

            // Manual HTTP used to fan out every LoggedIn seller at once. Cap with the same
            // batch_size as the light new-orders loop so proxy / 4Seller stay within budget.
            if (request.MaxParallelOwners is not > 0)
            {
                var values = await settings.GetValuesAsync(
                    [OrderSyncSettingKeys.NewBatchSize],
                    CancellationToken.None);
                request.MaxParallelOwners = OrderSyncScheduleConfig.ParseNewOrders(values).BatchSize;
            }

            var result = await syncService.SyncAsync(request, CancellationToken.None);
            job.Complete(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order sync job {JobId} failed", job.JobId);
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

    private sealed class JobState
    {
        private readonly object _gate = new();

        private string _state = OrderSyncJobStates.Running;
        private DateTime? _finishedAtUtc;
        private int _syncedSellers;
        private int _inserted;
        private int _updated;
        private List<OrderSyncSellerFailureDto> _failedSellers = [];
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

        public void Complete(OrderSyncResultDto result)
        {
            lock (_gate)
            {
                _state = OrderSyncJobStates.Completed;
                _finishedAtUtc = DateTime.UtcNow;
                _syncedSellers = result.SyncedSellers;
                _inserted = result.Inserted;
                _updated = result.Updated;
                _failedSellers = result.FailedSellers
                    .Select(f => new OrderSyncSellerFailureDto
                    {
                        OwnerUserId = f.OwnerUserId,
                        OwnerName = f.OwnerName,
                        Message = f.Message
                    })
                    .ToList();
                _message = result.FailedSellers.Count > 0
                    ? $"Đồng bộ xong: +{result.Inserted}, cập nhật {result.Updated}; {result.FailedSellers.Count} seller lỗi."
                    : $"Đồng bộ xong: +{result.Inserted}, cập nhật {result.Updated}.";
            }
        }

        public void Fail(Exception ex)
        {
            lock (_gate)
            {
                _state = OrderSyncJobStates.Failed;
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

        public OrderSyncJobStatusDto Snapshot()
        {
            lock (_gate)
            {
                return new OrderSyncJobStatusDto
                {
                    JobId = JobId,
                    State = _state,
                    StartedAtUtc = StartedAtUtc,
                    FinishedAtUtc = _finishedAtUtc,
                    SyncedSellers = _syncedSellers,
                    Inserted = _inserted,
                    Updated = _updated,
                    FailedSellers = _failedSellers
                        .Select(f => new OrderSyncSellerFailureDto
                        {
                            OwnerUserId = f.OwnerUserId,
                            OwnerName = f.OwnerName,
                            Message = f.Message
                        })
                        .ToList(),
                    Message = _message,
                    Error = _error
                };
            }
        }
    }
}
