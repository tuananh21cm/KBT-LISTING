namespace Application.Interfaces;

/// <summary>
/// Supplies pooled <see cref="System.Net.Http.HttpClient"/> instances bound to a specific
/// egress proxy (or direct) for 4Seller calls. Clients are long-lived and reused per proxy.
/// </summary>
public interface IFourSellerHttpClientProvider
{
    /// <summary>
    /// Returns a cached client whose handler routes through <paramref name="proxyUrl"/>.
    /// Null/empty/unparseable → a direct (no-proxy) client.
    /// </summary>
    System.Net.Http.HttpClient GetClient(string? proxyUrl);
}
