using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>TikTok Shop Growth Center violation/warning records.</summary>
public interface ITikTokShopHealthRecordService
{
    /// <summary>Ingest records từ extension — dedupe theo <c>RecordId</c>.</summary>
    Task<SyncTikTokShopHealthRecordsResultDto> SyncAsync(
        Guid ownerId,
        SyncTikTokShopHealthRecordsDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Tóm tắt active counts + recent cho nhiều shop (board API).</summary>
    Task<IReadOnlyDictionary<Guid, ShopHealthSummaryDto>> GetSummariesForShopsAsync(
        IReadOnlyList<Guid> shopIds,
        CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu một record đã xử lý trên UI nhiệm vụ.</summary>
    Task MarkHandledAsync(
        Guid? ownerScope,
        MarkShopHealthRecordHandledDto dto,
        CancellationToken cancellationToken = default);
}
