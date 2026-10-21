using Application.DTOs.Order;

namespace Application.Interfaces;

/// <summary>
/// Bulk push of USPS tracking numbers to PrintPackShip (create batch → send).
/// </summary>
public interface IUspsScanPushService
{
    /// <summary>
    /// Filters eligible orders, creates a remote batch, persists it, then sends (charges).
    /// </summary>
    /// <param name="orderIds">Selected order ids.</param>
    /// <param name="notes">Optional create-batch notes.</param>
    /// <param name="actingUserId">User who triggered the push.</param>
    /// <param name="ownerUserId">When set, only that seller's orders are eligible.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<UspsScanPushResultDto> PushAsync(
        IReadOnlyList<Guid> orderIds,
        string? notes,
        Guid actingUserId,
        Guid? ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Admin list of local USPS scan batches.</summary>
    Task<UspsScanBatchListPageDto> ListBatchesAsync(
        UspsScanBatchListQuery query,
        CancellationToken cancellationToken = default);
}
