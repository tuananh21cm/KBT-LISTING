namespace Application.Interfaces;

/// <summary>
/// Process-wide cache of recently validated 4Seller sessions.
/// Keyed by user + cookie fingerprint so a cookie update invalidates automatically.
/// Singleton; in-memory only.
/// </summary>
public interface IFourSellerSessionCache
{
    /// <summary>
    /// Returns true when this user+cookie was marked valid within the configured TTL.
    /// </summary>
    bool TryGetValid(Guid userId, string cookie);

    /// <summary>Records a successful is-login probe for the given user+cookie.</summary>
    void MarkValid(Guid userId, string cookie);

    /// <summary>Drops any cached entry for the user (cookie change or expired latch).</summary>
    void Invalidate(Guid userId);
}
