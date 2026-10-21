namespace Application.DTOs.Shop;

/// <summary>Extension heartbeat — keep or open an online session.</summary>
public class ShopOnlineHeartbeatDto
{
    /// <summary>TikTok seller id (= <c>Shop.ExternalShopId</c>).</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>UUID from the extension for this browser online window.</summary>
    public string ClientSessionId { get; set; } = string.Empty;

    /// <summary>UTC session start (client clock).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC last-seen watermark (client clock).</summary>
    public DateTime LastSeenAt { get; set; }
}

/// <summary>Extension close — end an online session.</summary>
public class ShopOnlineCloseDto
{
    /// <summary>TikTok seller id (= <c>Shop.ExternalShopId</c>).</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>UUID matching the open session.</summary>
    public string ClientSessionId { get; set; } = string.Empty;

    /// <summary>UTC end time.</summary>
    public DateTime EndedAt { get; set; }

    /// <summary>
    /// Optional close reason — see <c>ShopOnlineCloseReasons</c>.
    /// Defaults to <c>Explicit</c> when omitted/invalid.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>Result of heartbeat / close.</summary>
public class ShopOnlineSessionDto
{
    /// <summary>Server row id.</summary>
    public Guid Id { get; set; }

    /// <summary>Local shop id.</summary>
    public Guid ShopId { get; set; }

    /// <summary>TikTok seller id.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>
    /// Client session id — may rotate after Vietnam day-boundary split.
    /// Extension must adopt this value when different from the request.
    /// </summary>
    public string ClientSessionId { get; set; } = string.Empty;

    /// <summary>UTC start.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC last heartbeat.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>UTC end; null while open.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Close reason when ended.</summary>
    public string? CloseReason { get; set; }

    /// <summary>True when <see cref="EndedAt"/> is null.</summary>
    public bool IsOpen { get; set; }
}
