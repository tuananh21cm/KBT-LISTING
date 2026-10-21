namespace Domain.Exceptions;

/// <summary>
/// Thrown when 4Seller's EdgeOne WAF blocks the request at the network/IP level
/// (HTTP 567 "Restricted Access"). This is distinct from an expired cookie: callers
/// must NOT mark the seller cookie as expired on this exception.
/// </summary>
public class FourSellerBlockedException : BusinessException
{
    /// <summary>
    /// Initializes the exception with the proxy that was blocked (null = direct egress).
    /// </summary>
    public FourSellerBlockedException(string message, string? proxyUrl) : base(message)
    {
        ProxyUrl = proxyUrl;
    }

    /// <summary>Proxy URL that received the block, or null when calling direct.</summary>
    public string? ProxyUrl { get; }
}
