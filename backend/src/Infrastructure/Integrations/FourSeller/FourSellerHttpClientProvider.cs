using System.Collections.Concurrent;
using System.Net;
using Application.Interfaces;
using Microsoft.Extensions.Logging;
using Shared.Helpers;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Singleton pool of <see cref="HttpClient"/> keyed by egress proxy. Each distinct proxy
/// (and the direct/no-proxy case) gets one long-lived client + handler, reused across
/// requests to avoid socket exhaustion. Cookies are sent explicitly per request.
/// </summary>
public sealed class FourSellerHttpClientProvider : IFourSellerHttpClientProvider, IDisposable
{
    private static readonly Uri BaseAddress = new("https://www.4seller.com");

    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
    private readonly ILogger<FourSellerHttpClientProvider> _logger;

    /// <summary>Initializes the provider.</summary>
    public FourSellerHttpClientProvider(ILogger<FourSellerHttpClientProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public HttpClient GetClient(string? proxyUrl)
    {
        var key = string.IsNullOrWhiteSpace(proxyUrl) ? string.Empty : proxyUrl.Trim();
        return _clients.GetOrAdd(key, CreateClient);
    }

    private HttpClient CreateClient(string key)
    {
        var handler = new SocketsHttpHandler
        {
            // Cookie header is set explicitly per request (per-seller session).
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = string.IsNullOrEmpty(key)
                ? TimeSpan.FromSeconds(30)
                : TimeSpan.FromSeconds(12)
        };

        if (!string.IsNullOrEmpty(key))
        {
            if (EgressProxyParser.TryBuild(key, out var proxy))
            {
                handler.Proxy = proxy;
                handler.UseProxy = true;
            }
            else
            {
                _logger.LogWarning("4Seller proxy '{Proxy}' is not a valid proxy URL; using direct egress", key);
                handler.UseProxy = false;
            }
        }
        else
        {
            handler.UseProxy = false;
        }

        return new HttpClient(handler)
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(100)
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
    }
}
