namespace Application.Interfaces;

/// <summary>
/// Supplies pooled <see cref="System.Net.Http.HttpClient"/> instances bound to a specific egress
/// proxy for SHEIN storefront calls, so each fulfil account can leave from its own IP.
/// </summary>
public interface ISheinHttpClientProvider
{
    /// <summary>
    /// Returns a cached client whose handler routes through <paramref name="proxyUrl"/>.
    /// Null/empty/unparseable → a direct (no-proxy) client.
    /// </summary>
    System.Net.Http.HttpClient GetClient(string? proxyUrl);
}
