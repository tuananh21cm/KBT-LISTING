namespace Domain.Entities;

/// <summary>
/// One continuous online window for a TikTok shop while Chrome has a seller-us tab open.
/// <see cref="EndedAt"/> null means the session is still open; <see cref="LastSeenAt"/> is the
/// heartbeat watermark used to close after browser kill / timeout.
/// </summary>
public class ShopOnlineSession
{
    /// <summary>Row identifier.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Local shop FK.</summary>
    public Guid ShopId { get; set; }

    /// <summary>TikTok seller id (= <c>Shop.ExternalShopId</c>), denormalized for ingest.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>UUID from the extension — idempotent open/close per browser session.</summary>
    public string ClientSessionId { get; set; } = string.Empty;

    /// <summary>UTC session start.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC last heartbeat from the extension.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>UTC session end; null while still open.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Why the session closed — see <c>ShopOnlineCloseReasons</c>.
    /// Null while open.
    /// </summary>
    public string? CloseReason { get; set; }

    /// <summary>Shop navigation.</summary>
    public Shop Shop { get; set; } = null!;
}
