using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>
/// Syncs TikTok Shop payouts from the browser extension into receiving-bank ledger + order flags.
/// </summary>
public interface ITikTokPayoutSyncService
{
    /// <summary>Resolved Settings for extension Auto Sync.</summary>
    Task<TikTokPayoutSyncConfigDto> GetSyncConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest TikTokPayout ledger cursor for the owner's shop matched by externalShopId.
    /// </summary>
    Task<TikTokPayoutSyncCursorDto> GetCursorAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert payout ledger rows + mark linked orders settlement-paid (no SettlementPaid ledger rows).
    /// </summary>
    Task<SyncTikTokPayoutsResultDto> SyncAsync(
        Guid ownerId,
        SyncTikTokPayoutsDto dto,
        CancellationToken cancellationToken = default);
}
