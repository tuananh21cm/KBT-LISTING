namespace Domain.Exceptions;

/// <summary>
/// Thrown when a 4Seller call cannot complete for a transient reason
/// (proxy connect failure, network timeout, rate limit, non-JSON body, API code != 0).
/// Distinct from an expired cookie: callers must NOT mark the seller cookie as expired.
/// </summary>
public class FourSellerUnavailableException : BusinessException
{
    /// <summary>Initializes the exception.</summary>
    public FourSellerUnavailableException(string message, string? proxyUrl = null) : base(message)
    {
        ProxyUrl = proxyUrl;
    }

    /// <summary>Proxy URL in use when the failure occurred, or null for direct egress.</summary>
    public string? ProxyUrl { get; }
}
