using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>Ingest điểm Health Center (AHR + Shop Performance Score) từ extension.</summary>
public interface ITikTokShopScoreService
{
    /// <summary>Ghi snapshot điểm lên bảng Shops cho shop thuộc owner.</summary>
    Task<SyncTikTokShopScoresResultDto> SyncAsync(
        Guid ownerId,
        SyncTikTokShopScoresDto dto,
        CancellationToken cancellationToken = default);
}
