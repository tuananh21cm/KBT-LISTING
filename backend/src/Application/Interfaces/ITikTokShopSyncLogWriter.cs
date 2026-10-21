namespace Application.Interfaces;

/// <summary>
/// Fail-safe writer for <see cref="Domain.Entities.TikTokShopSyncLog"/> rows.
/// </summary>
public interface ITikTokShopSyncLogWriter
{
    /// <summary>Append a sync log row. Errors are swallowed (logged as warning).</summary>
    Task WriteAsync(
        Guid ownerId,
        string? runId,
        string externalShopId,
        Guid? shopId,
        string syncType,
        string status,
        string? errorMessage = null,
        string source = Shared.Constants.TikTokShopSyncSources.Manual,
        CancellationToken cancellationToken = default);
}
