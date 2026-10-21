using Application.Interfaces;
using Application.Options;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Loads all eligible USPS orders and pushes them via <see cref="IUspsScanPushService"/> in chunks,
/// gated on US post-office business hours.
/// </summary>
public sealed class UspsScanAutoPushOrchestrator : IUspsScanAutoPushOrchestrator
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;
    private readonly IUspsScanPushService _push;
    private readonly IUspsScanAutoActingUserResolver _actingUser;
    private readonly PrintPackShipOptions _options;
    private readonly ILogger<UspsScanAutoPushOrchestrator> _logger;

    /// <summary>Creates the orchestrator.</summary>
    public UspsScanAutoPushOrchestrator(
        IApplicationDbContext db,
        ISettingProvider settings,
        IUspsScanPushService push,
        IUspsScanAutoActingUserResolver actingUser,
        IOptions<PrintPackShipOptions> options,
        ILogger<UspsScanAutoPushOrchestrator> logger)
    {
        _db = db;
        _settings = settings;
        _push = push;
        _actingUser = actingUser;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        UspsScanAutoSettings config;
        try
        {
            config = await UspsScanAutoSettingsReader.ReadAsync(_settings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USPS auto scan could not read settings");
            return;
        }

        await RunOnceAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(UspsScanAutoSettings config, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunOnceCoreAsync(config, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USPS auto scan cycle failed unexpectedly");
        }
    }

    private async Task RunOnceCoreAsync(UspsScanAutoSettings config, CancellationToken cancellationToken)
    {
        if (!config.AutoEnabled)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;

        // Window first: outside US business hours nobody scans the labels, so skip before touching Orders.
        if (!UspsScanWindow.IsOpen(nowUtc, config.Window))
        {
            _logger.LogDebug(
                "USPS auto scan: outside US business hours, next open {NextOpenUtc:u}",
                UspsScanWindow.NextOpenUtc(nowUtc, config.Window));
            return;
        }

        var maxPerBatch = _options.MaxTrackingPerBatch > 0 ? _options.MaxTrackingPerBatch : 100;
        // >0: user chunk, capped by provider max. 0: one logical "send all" (split only by provider max).
        var chunkSize = config.ChunkSize == 0
            ? 0
            : Math.Min(config.ChunkSize, maxPerBatch);

        var actingUserId = await _actingUser.ResolveActingAdminUserIdAsync(cancellationToken);
        if (actingUserId is null)
        {
            _logger.LogWarning("USPS auto scan skipped: no active Admin user");
            return;
        }

        var cutoff = nowUtc.AddHours(-config.DelayHours);
        var hardCap = UspsScanAutoSettingKeys.HardCapPerCycle;

        var ids = await _db.Orders
            .AsNoTracking()
            .Where(o => o.HasUspsSignal
                && o.UspsProcessedAt == null
                && o.UspsScanBatchId == null
                && o.TrackingNo != null
                && o.TrackingNo != ""
                && o.TrackingAddedAt != null
                && o.TrackingAddedAt <= cutoff
                && o.InTransitAt == null
                && o.DeliveredAt == null
                && o.PlatformOrderStatus != PlatformOrderStatuses.InTransit
                && o.PlatformOrderStatus != PlatformOrderStatuses.Delivered
                && o.PlatformOrderStatus != PlatformOrderStatuses.Refund
                && o.PlatformOrderStatus != PlatformOrderStatuses.Cancelled
                && o.WorkflowTab != OrderWorkflowTabs.Refund
                && o.WorkflowTab != OrderWorkflowTabs.Cancelled
                && o.WorkflowTab != OrderWorkflowTabs.InTransit
                && o.WorkflowTab != OrderWorkflowTabs.Delivered)
            .OrderBy(o => o.TrackingAddedAt)
            .Select(o => o.Id)
            .Take(hardCap)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            _logger.LogDebug("USPS auto scan: no eligible orders (min age={DelayHours}h)", config.DelayHours);
            return;
        }

        if (ids.Count >= hardCap)
        {
            _logger.LogWarning(
                "USPS auto scan: eligible queue hit hard cap {HardCap}; remainder deferred to next cycle",
                hardCap);
        }

        var effectiveChunk = chunkSize == 0 ? maxPerBatch : chunkSize;
        if (chunkSize == 0 && ids.Count > maxPerBatch)
        {
            _logger.LogWarning(
                "USPS auto scan: chunk_size=0 but {Count} > MaxTrackingPerBatch {Max}; splitting by provider max",
                ids.Count,
                maxPerBatch);
        }

        var chunks = ids.Chunk(effectiveChunk).ToList();
        _logger.LogInformation(
            "USPS auto scan: {Count} eligible, chunk_size={ChunkSize} (effective={Effective}), {Chunks} PushAsync call(s)",
            ids.Count,
            config.ChunkSize,
            effectiveChunk,
            chunks.Count);

        for (var index = 0; index < chunks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A long queue can run past the cutoff margin; drop the rest rather than push
            // labels nobody will scan today.
            if (index > 0 && !UspsScanWindow.IsOpen(DateTime.UtcNow, config.Window))
            {
                _logger.LogInformation(
                    "USPS auto scan: window closed mid-cycle, {Remaining} chunk(s) deferred to next window",
                    chunks.Count - index);
                return;
            }

            var chunkList = chunks[index].ToList();
            try
            {
                var result = await _push.PushAsync(
                    chunkList,
                    notes: "auto",
                    actingUserId.Value,
                    ownerUserId: null,
                    cancellationToken);

                _logger.LogInformation(
                    "USPS auto scan chunk: requested={Requested} sent={Sent} status={Status} skipped={Skipped}",
                    chunkList.Count,
                    result.SentCount,
                    result.Status,
                    result.Skipped.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "USPS auto scan chunk failed ({Count} ids); continuing remaining chunks",
                    chunkList.Count);
            }
        }
    }
}
