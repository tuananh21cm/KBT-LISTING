using Application.Interfaces;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Fail-safe append-only writer for TikTok Shop extension sync logs.
/// </summary>
public class TikTokShopSyncLogWriter : ITikTokShopSyncLogWriter
{
    private const int MaxRunIdLength = 64;
    private const int MaxExternalShopIdLength = 64;
    private const int MaxErrorLength = 500;

    private readonly IApplicationDbContext _db;
    private readonly ILogger<TikTokShopSyncLogWriter> _logger;

    /// <summary>Creates the writer.</summary>
    public TikTokShopSyncLogWriter(IApplicationDbContext db, ILogger<TikTokShopSyncLogWriter> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        Guid ownerId,
        string? runId,
        string externalShopId,
        Guid? shopId,
        string syncType,
        string status,
        string? errorMessage = null,
        string source = TikTokShopSyncSources.Manual,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedRunId = NormalizeRunId(runId);
            if (normalizedRunId.Length == 0)
            {
                return;
            }

            _db.TikTokShopSyncLogs.Add(new TikTokShopSyncLog
            {
                Id = Guid.NewGuid(),
                RunId = normalizedRunId,
                OwnerId = ownerId,
                ShopId = shopId,
                ExternalShopId = Truncate(externalShopId.Trim(), MaxExternalShopIdLength),
                SyncType = syncType.Trim(),
                Status = status.Trim(),
                ErrorMessage = Truncate(errorMessage?.Trim(), MaxErrorLength),
                SyncedAt = DateTime.UtcNow,
                Source = string.IsNullOrWhiteSpace(source)
                    ? TikTokShopSyncSources.Manual
                    : source.Trim()
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Không ghi được TikTokShopSyncLog (owner={OwnerId}, type={SyncType}, status={Status}).",
                ownerId,
                syncType,
                status);
        }
    }

    private static string NormalizeRunId(string? runId)
    {
        var value = (runId ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        return value.Length <= MaxRunIdLength ? value : value[..MaxRunIdLength];
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
