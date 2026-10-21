using Application.Services;

namespace Application.Interfaces;

/// <summary>
/// One scan cycle: load all eligible USPS tracks, push in chunks via PrintPackShip.
/// </summary>
public interface IUspsScanAutoPushOrchestrator
{
    /// <summary>
    /// Runs a single auto-push cycle when enabled, reading settings itself.
    /// Does not apply the inter-cycle interval.
    /// </summary>
    Task RunOnceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a single auto-push cycle against an already-loaded settings snapshot, so the hosted
    /// loop does not re-query <c>order.usps_scan.*</c>. Does not apply the inter-cycle interval.
    /// </summary>
    Task RunOnceAsync(UspsScanAutoSettings config, CancellationToken cancellationToken = default);
}
