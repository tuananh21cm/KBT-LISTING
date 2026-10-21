namespace Application.Interfaces;

using Application.DTOs.Shop;

/// <summary>Read/write TikTok Shop extension sync logs.</summary>
public interface ITikTokShopSyncLogService
{
    /// <summary>Record a skipped/failed step from extension without running sync.</summary>
    Task<TikTokShopSyncLogDto> RecordAsync(
        Guid ownerId,
        RecordTikTokShopSyncLogDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>List sync logs for the current owner.</summary>
    Task<TikTokShopSyncLogPageDto> ListAsync(
        Guid ownerId,
        TikTokShopSyncLogQuery query,
        CancellationToken cancellationToken = default);
}
