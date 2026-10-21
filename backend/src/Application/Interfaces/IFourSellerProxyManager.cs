namespace Application.Interfaces;

/// <summary>
/// Resolves the egress proxy for a seller's 4Seller calls (sticky per seller, with a
/// shared failover pool) and tracks per-proxy block cooldowns. Singleton, in-memory state.
/// </summary>
public interface IFourSellerProxyManager
{
    /// <summary>
    /// Returns the effective proxy URL for a seller, or null for direct egress.
    /// Absolute sticky: when the seller has a personal proxy it is ALWAYS returned
    /// (even during cooldown) — jumping IP mid-session can kill the 4Seller cookie.
    /// The Settings master toggle only enables the shared pool for sellers without a
    /// personal proxy. Returns null when neither applies.
    /// </summary>
    Task<string?> ResolveAsync(Guid userId, string? userProxy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a proxy as unusable so it enters cooldown.
    /// <paramref name="reason"/> is logged (<c>567</c>, <c>connect</c>, <c>407</c>, …).
    /// </summary>
    void ReportBlocked(string? proxyUrl, string reason = "block");

    /// <summary>Clears any cooldown after a successful call through the proxy.</summary>
    void ReportSuccess(string? proxyUrl);
}
