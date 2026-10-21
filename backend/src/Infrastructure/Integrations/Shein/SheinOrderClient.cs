using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Shein;
using Application.Interfaces;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Infrastructure.Integrations.Shein;

/// <summary>
/// Server-side replay of the SHEIN storefront order APIs. The captured headers are session
/// tokens rather than per-request signatures, which is why the same set works across pages and
/// billnos — the extension already relies on that when it paginates.
/// </summary>
public sealed class SheinOrderClient : ISheinOrderClient
{
    /// <summary>Browser UA used when the capture did not include one; SHEIN rejects bare clients.</summary>
    private const string FallbackUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/131.0.0.0 Safari/537.36";

    private readonly ISheinHttpClientProvider _clients;
    private readonly ILogger<SheinOrderClient> _logger;

    /// <summary>Initializes the client.</summary>
    public SheinOrderClient(ISheinHttpClientProvider clients, ILogger<SheinOrderClient> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SheinApiResult> FetchOrderListPageAsync(
        SheinSessionContext session,
        int page,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var url = $"{session.Origin}/bff-api/order/list?_ver=1.1.8&_lang=en&page={page}&limit={limit}";
        return SendAsync(session, url, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SheinApiResult> FetchOrderDetailAsync(
        SheinSessionContext session,
        string billno,
        CancellationToken cancellationToken = default)
    {
        var url = $"{session.Origin}/api/orders/base/get_order_info/get" +
                  $"?_ver=1.1.8&_lang=en&billno={Uri.EscapeDataString(billno)}";
        return SendAsync(session, url, cancellationToken);
    }

    private async Task<SheinApiResult> SendAsync(
        SheinSessionContext session,
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplySessionHeaders(request, session);

        var client = _clients.GetClient(session.Proxy);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SheinApiResult.Failure(
                $"SHEIN không phản hồi trong {client.Timeout.TotalSeconds:0}s (tài khoản {session.AccountName}).",
                proxyFailed: session.Proxy is not null);
        }
        catch (HttpRequestException ex)
        {
            return SheinApiResult.Failure(
                $"Không gọi được SHEIN cho {session.AccountName}: {ex.Message}",
                proxyFailed: session.Proxy is not null);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                // 401/403 are the session dying; a proxy that is blocked answers 407/5xx instead.
                var sessionInvalid = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                var proxyFailed = session.Proxy is not null &&
                                  response.StatusCode is HttpStatusCode.ProxyAuthenticationRequired
                                      or HttpStatusCode.BadGateway
                                      or HttpStatusCode.ServiceUnavailable;
                return SheinApiResult.Failure(
                    $"SHEIN trả HTTP {status} cho {session.AccountName}.",
                    sessionInvalid,
                    proxyFailed);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? payload;
            try
            {
                payload = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                // An HTML body here is the risk-control / captcha page rather than the API.
                _logger.LogWarning(
                    "SHEIN returned a non-JSON body for account {Account}; session likely challenged",
                    session.AccountName);
                return SheinApiResult.Failure(
                    $"SHEIN trả về trang xác minh thay vì JSON cho {session.AccountName} — session cần bắt lại.",
                    sessionInvalid: true);
            }

            var code = payload?["code"]?.ToString();
            if (!string.IsNullOrEmpty(code) && code != "0")
            {
                var msg = payload?["msg"]?.ToString() ?? payload?["tips"]?.ToString() ?? string.Empty;
                // SHEIN uses the 30xxxxx family for "not logged in" / risk challenges.
                var sessionInvalid = code.StartsWith("30", StringComparison.Ordinal) ||
                                     msg.Contains("login", StringComparison.OrdinalIgnoreCase);
                return SheinApiResult.Failure(
                    $"SHEIN trả code {code} cho {session.AccountName}: {msg}".TrimEnd(':', ' '),
                    sessionInvalid);
            }

            return SheinApiResult.Success(payload);
        }
    }

    private static void ApplySessionHeaders(HttpRequestMessage request, SheinSessionContext session)
    {
        request.Headers.TryAddWithoutValidation("accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("x-requested-with", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("referer", $"{session.Origin}/user/orders/list");

        foreach (var (name, value) in session.Headers)
        {
            if (SheinSessionKeys.DeniedHeaders.Contains(name) ||
                name.StartsWith("content-", StringComparison.OrdinalIgnoreCase))
            {
                // Content headers belong on HttpContent; HttpRequestMessage.Headers.Remove throws for them.
                continue;
            }

            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        if (!request.Headers.Contains("user-agent"))
        {
            request.Headers.TryAddWithoutValidation("user-agent", FallbackUserAgent);
        }

        request.Headers.TryAddWithoutValidation("cookie", session.Cookie);
    }
}
