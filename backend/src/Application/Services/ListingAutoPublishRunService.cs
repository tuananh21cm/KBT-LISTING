using System.Text.Json;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>CRUD for auto publish run logs.</summary>
public sealed class ListingAutoPublishRunService : IListingAutoPublishRunService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IApplicationDbContext _db;

    public ListingAutoPublishRunService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<ListingAutoPublishRunDto>> ListRunsAsync(
        Guid listingId,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var runs = await _db.ListingAutoPublishRuns.AsNoTracking()
            .Where(r => r.ListingId == listingId)
            .OrderByDescending(r => r.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return runs.Select(Map).ToList();
    }

    public async Task<ListingAutoPublishRunDto?> GetRunAsync(
        Guid listingId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _db.ListingAutoPublishRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ListingId == listingId && r.Id == runId, cancellationToken);
        return run is null ? null : Map(run);
    }

    public async Task RetryAutoPublishAsync(Guid listingId, CancellationToken cancellationToken = default)
    {
        var listing = await _db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, cancellationToken)
                      ?? throw new NotFoundException("Không tìm thấy listing.");

        if (!string.Equals(listing.PageType, ListingPageTypes.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Chỉ retry auto publish cho listing draft.");
        }

        if (PublishStatuses.IsInFlight(listing.PublishStatus))
        {
            throw new BusinessException("Listing đang publish in-flight — không thể retry.");
        }

        var staleCutoff = DateTime.UtcNow - ListingAutoPublishStaleRunRecovery.StaleThreshold;
        var hasActiveRun = await _db.ListingAutoPublishRuns.AsNoTracking()
            .AnyAsync(
                r => r.ListingId == listingId
                     && r.Status == ListingAutoPublishRunStatuses.Running
                     && r.StartedAt >= staleCutoff,
                cancellationToken);
        if (hasActiveRun)
        {
            throw new BusinessException("Listing đang có auto publish run đang chạy.");
        }

        listing.AutoPublishSkipped = false;
        listing.AutoPublishSkippedAt = null;
        listing.AutoPublishSkipReason = null;
        // Failed was excluded from pick after silent demote — clear so FIFO can take this draft again.
        if (PublishStatuses.Normalize(listing.PublishStatus) == PublishStatuses.Failed)
        {
            listing.PublishStatus = PublishStatuses.Draft;
        }

        // Drop step checkpoints so the next pick re-runs load_full (4Seller refresh) through publish.
        listing.AutoPublishCompletedStepsJson = null;
        listing.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ListingAutoPublishRunDto Map(Domain.Entities.ListingAutoPublishRun run)
    {
        var dto = new ListingAutoPublishRunDto
        {
            Id = run.Id,
            ListingId = run.ListingId,
            ShopId = run.ShopId,
            Status = run.Status,
            StartedAt = run.StartedAt,
            FinishedAt = run.FinishedAt,
            DurationMs = run.DurationMs,
            FailedStepCode = run.FailedStepCode,
            ErrorMessage = run.ErrorMessage
        };

        if (!string.IsNullOrWhiteSpace(run.StepsJson))
        {
            try
            {
                dto.Steps = JsonSerializer.Deserialize<List<ListingAutoPublishStepDto>>(run.StepsJson, JsonOpts)
                            ?? [];
            }
            catch (JsonException)
            {
                dto.Steps = [];
            }
        }

        return dto;
    }
}
