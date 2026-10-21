using Application.DTOs.Promotion;

namespace Application.Interfaces;

/// <summary>
/// Runs manual product-discount syncs in the background; UI polls <see cref="GetStatus"/>.
/// </summary>
public interface IProductDiscountSyncJobRunner
{
    /// <summary>Queues a sync and returns the initial snapshot.</summary>
    ProductDiscountSyncJobStatusDto Start(ProductDiscountSyncRequest request, Guid? startedByUserId);

    /// <summary>Current snapshot, or null when unknown / expired.</summary>
    ProductDiscountSyncJobStatusDto? GetStatus(Guid jobId);

    /// <summary>Who started the job (seller isolation when polling).</summary>
    Guid? GetStartedByUserId(Guid jobId);
}
