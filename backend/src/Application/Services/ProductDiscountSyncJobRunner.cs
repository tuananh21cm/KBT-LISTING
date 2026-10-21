using System.Collections.Concurrent;
using Application.DTOs.Promotion;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// In-memory product-discount sync jobs (single API process). Manual sync returns 202 + jobId;
/// the UI polls status instead of holding the HTTP connection open.
/// </summary>
public sealed class ProductDiscountSyncJobRunner : IProductDiscountSyncJobRunner
{
    private static readonly TimeSpan CompletedJobTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AbandonedJobTtl = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<Guid, JobState> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProductDiscountSyncJobRunner> _logger;

    /// <summary>Initializes the runner.</summary>
    public ProductDiscountSyncJobRunner(
        IServiceScopeFactory scopeFactory,
        ILogger<ProductDiscountSyncJobRunner> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public ProductDiscountSyncJobStatusDto Start(ProductDiscountSyncRequest request, Guid? startedByUserId)
    {
        PruneExpired();

        var job = new JobState(Guid.NewGuid(), startedByUserId);
        _jobs[job.JobId] = job;
        _ = Task.Run(() => RunAsync(job, request));
        return job.Snapshot();
    }

    /// <inheritdoc />
    public ProductDiscountSyncJobStatusDto? GetStatus(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job.Snapshot() : null;

    /// <inheritdoc />
    public Guid? GetStartedByUserId(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job.StartedByUserId : null;

    private async Task RunAsync(JobState job, ProductDiscountSyncRequest request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<IProductDiscountSyncService>();
            var progress = new SellerProgressSink(job.ApplySellerProgress);
            var result = await syncService.SyncAsync(request, progress, CancellationToken.None);
            job.Complete(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Product discount sync job {JobId} failed", job.JobId);
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

    private sealed class SellerProgressSink : IProgress<ProductDiscountSyncSellerProgressDto>
    {
        private readonly Action<ProductDiscountSyncSellerProgressDto> _handler;

        public SellerProgressSink(Action<ProductDiscountSyncSellerProgressDto> handler) => _handler = handler;

        public void Report(ProductDiscountSyncSellerProgressDto value) => _handler(value);
    }

    private sealed class JobState
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, ProductDiscountSyncSellerProgressDto> _sellers = [];
        private readonly List<Guid> _sellerOrder = [];

        private string _state = ProductDiscountSyncJobStates.Running;
        private DateTime? _finishedAtUtc;
        private int _syncedSellers;
        private int _inserted;
        private int _updated;
        private int _skipped;
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

        public void ApplySellerProgress(ProductDiscountSyncSellerProgressDto update)
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
                existing.Skipped = update.Skipped;
                existing.Message = update.Message;
                if (update.ElapsedMs > 0)
                {
                    existing.ElapsedMs = update.ElapsedMs;
                }
            }
        }

        public void Complete(ProductDiscountSyncResultDto result)
        {
            lock (_gate)
            {
                _state = ProductDiscountSyncJobStates.Completed;
                _finishedAtUtc = DateTime.UtcNow;
                _syncedSellers = result.SyncedSellers;
                _inserted = result.Inserted;
                _updated = result.Updated;
                _skipped = result.Skipped;
                _message = result.FailedSellers.Count > 0
                    ? $"Đồng bộ xong: +{result.Inserted}, cập nhật {result.Updated}, bỏ qua {result.Skipped}; {result.FailedSellers.Count} seller lỗi."
                    : $"Đồng bộ xong: +{result.Inserted}, cập nhật {result.Updated}, bỏ qua {result.Skipped}.";

                foreach (var failure in result.FailedSellers)
                {
                    if (_sellers.TryGetValue(failure.OwnerUserId, out var seller))
                    {
                        seller.Status = ProductDiscountSyncSellerStatuses.Failed;
                        seller.Message = failure.Message;
                        continue;
                    }

                    _sellers[failure.OwnerUserId] = new ProductDiscountSyncSellerProgressDto
                    {
                        OwnerUserId = failure.OwnerUserId,
                        OwnerName = failure.OwnerName,
                        Status = ProductDiscountSyncSellerStatuses.Failed,
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
                _state = ProductDiscountSyncJobStates.Failed;
                _finishedAtUtc = DateTime.UtcNow;
                _error = ex.Message;
                _message = "Đồng bộ product discount thất bại.";
            }
        }

        public bool IsExpired(DateTime now, TimeSpan completedTtl, TimeSpan abandonedTtl)
        {
            lock (_gate)
            {
                if (_finishedAtUtc is DateTime finished)
                {
                    return now - finished > completedTtl;
                }

                return now - StartedAtUtc > abandonedTtl;
            }
        }

        public ProductDiscountSyncJobStatusDto Snapshot()
        {
            lock (_gate)
            {
                var sellers = _sellerOrder
                    .Where(id => _sellers.ContainsKey(id))
                    .Select(id =>
                    {
                        var s = _sellers[id];
                        return new ProductDiscountSyncSellerProgressDto
                        {
                            OwnerUserId = s.OwnerUserId,
                            OwnerName = s.OwnerName,
                            Status = s.Status,
                            Inserted = s.Inserted,
                            Updated = s.Updated,
                            Skipped = s.Skipped,
                            ElapsedMs = s.ElapsedMs,
                            Message = s.Message
                        };
                    })
                    .ToList();

                var completed = sellers.Count(s =>
                    string.Equals(s.Status, ProductDiscountSyncSellerStatuses.Succeeded, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.Status, ProductDiscountSyncSellerStatuses.Failed, StringComparison.OrdinalIgnoreCase));

                return new ProductDiscountSyncJobStatusDto
                {
                    JobId = JobId,
                    State = _state,
                    StartedAtUtc = StartedAtUtc,
                    FinishedAtUtc = _finishedAtUtc,
                    TotalSellers = sellers.Count,
                    CompletedSellers = completed,
                    SyncedSellers = _syncedSellers,
                    Inserted = _inserted,
                    Updated = _updated,
                    Skipped = _skipped,
                    Message = _message,
                    Error = _error,
                    Sellers = sellers
                };
            }
        }
    }
}
