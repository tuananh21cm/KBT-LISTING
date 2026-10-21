using System.Collections.Concurrent;
using System.Net;
using Application.Interfaces;
using Microsoft.Extensions.Logging;
using Shared.Helpers;

namespace Infrastructure.Integrations.Shein;

/// <summary>
/// Singleton pool of <see cref="HttpClient"/> keyed by egress proxy for SHEIN storefront calls.
/// Each distinct proxy (and the direct case) gets one long-lived client + handler so a fleet of
/// fulfil accounts does not exhaust sockets. Cookies are sent explicitly per request.
/// </summary>
public sealed class SheinHttpClientProvider : ISheinHttpClientProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();
    private readonly ILogger<SheinHttpClientProvider> _logger;

    /// <summary>Initializes the provider.</summary>
    public SheinHttpClientProvider(ILogger<SheinHttpClientProvider> logger)
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
            // Cookie header is set explicitly per request (per fulfil account session).
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(20)
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
                _logger.LogWarning("SHEIN proxy '{Proxy}' is not a valid proxy URL; using direct egress", key);
                handler.UseProxy = false;
            }
        }
        else
        {
            handler.UseProxy = false;
        }

        return new HttpClient(handler)
        {
            // A hung proxy must not hold a sync slot; the caller retries on the next pass.
            Timeout = TimeSpan.FromSeconds(30)
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
