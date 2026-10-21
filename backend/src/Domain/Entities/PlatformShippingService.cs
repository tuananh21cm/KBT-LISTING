namespace Domain.Entities;

/// <summary>
/// Cached 4Seller platform shipping carrier from <c>list-platform-shipping-service</c>.
/// Shared catalog (not per-user); refreshed at most once per 24h.
/// </summary>
public class PlatformShippingService
{
    /// <summary>4Seller carrier row id (primary key).</summary>
    public long FourSellerId { get; set; }

    /// <summary>Carrier display name (shippingService in mark-ship).</summary>
    public string Carrier { get; set; } = string.Empty;

    /// <summary>Platform code (e.g. TIKTOK).</summary>
    public string? Platform { get; set; }

    /// <summary>Region (e.g. US).</summary>
    public string? Region { get; set; }

    /// <summary>UTC timestamp when this row was last synced from 4Seller.</summary>
    public DateTime SyncedAt { get; set; }
}
