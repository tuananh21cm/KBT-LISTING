using Application.DTOs.Shop;

namespace Application.Interfaces;

/// <summary>
/// Ingest online sessions from the TikTok Shop extension (no JWT — ExternalShopId only).
/// </summary>
public interface IShopOnlineSessionService
{
    /// <summary>Open or refresh a session; may split across Vietnam midnight.</summary>
    Task<ShopOnlineSessionDto> HeartbeatAsync(
        ShopOnlineHeartbeatDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Close a session (idempotent if already closed).</summary>
    Task<ShopOnlineSessionDto> CloseAsync(
        ShopOnlineCloseDto dto,
        CancellationToken cancellationToken = default);
}
