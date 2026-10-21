using System.Text.Json;
using System.Text.Json.Nodes;
using Shared.Constants;

namespace Application.DTOs.Shein;

/// <summary>
/// A SHEIN storefront session resolved from a fulfil account: the captured security headers,
/// the cookie, the storefront origin, and the egress proxy to leave from.
/// </summary>
public sealed class SheinSessionContext
{
    /// <summary>Fulfil account owning the session.</summary>
    public required Guid AccountId { get; init; }

    /// <summary>Account display name (for logs and error messages).</summary>
    public required string AccountName { get; init; }

    /// <summary>SHEIN account email.</summary>
    public string? AccountEmail { get; init; }

    /// <summary>Captured request headers keyed by lowercase name.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>Cookie header string.</summary>
    public required string Cookie { get; init; }

    /// <summary>Storefront origin, e.g. <c>https://us.shein.com</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>Egress proxy, or null for direct.</summary>
    public string? Proxy { get; init; }

    /// <summary>
    /// Builds a session from the columns stored on a fulfil account. Returns null when the stored
    /// session is unusable (missing headers or cookie) — the caller should skip that account
    /// rather than burn a request that SHEIN will reject anyway.
    /// </summary>
    public static SheinSessionContext? TryCreate(
        Guid accountId,
        string accountName,
        string? accountEmail,
        string? headersJson,
        string? cookie,
        string? origin,
        string? proxy)
    {
        if (string.IsNullOrWhiteSpace(headersJson) || string.IsNullOrWhiteSpace(cookie))
        {
            return null;
        }

        Dictionary<string, string>? headers;
        try
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (headers is null || SheinSessionKeys.MissingRequiredHeaders(headers).Count > 0)
        {
            return null;
        }

        return new SheinSessionContext
        {
            AccountId = accountId,
            AccountName = accountName,
            AccountEmail = accountEmail,
            Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase),
            Cookie = cookie.Trim(),
            Origin = string.IsNullOrWhiteSpace(origin) ? SheinSessionKeys.DefaultOrigin : origin.TrimEnd('/'),
            Proxy = string.IsNullOrWhiteSpace(proxy) ? null : proxy.Trim()
        };
    }
}

/// <summary>Outcome of one SHEIN storefront call.</summary>
public sealed class SheinApiResult
{
    /// <summary>True when SHEIN answered with HTTP 2xx and business code 0.</summary>
    public bool Ok { get; init; }

    /// <summary>Parsed response body when <see cref="Ok"/>.</summary>
    public JsonNode? Payload { get; init; }

    /// <summary>Failure reason in Vietnamese, surfaced to admins.</summary>
    public string? Error { get; init; }

    /// <summary>
    /// True when the failure means the stored session is dead (auth expired, captcha, risk
    /// challenge) rather than a transient network or proxy hiccup. Only this warrants clearing
    /// the session and asking the operator to re-capture.
    /// </summary>
    public bool SessionInvalid { get; init; }

    /// <summary>True when the failure looks like the proxy/IP being blocked or unreachable.</summary>
    public bool ProxyFailed { get; init; }

    /// <summary>Creates a success result.</summary>
    public static SheinApiResult Success(JsonNode? payload) => new() { Ok = true, Payload = payload };

    /// <summary>Creates a failure result.</summary>
    public static SheinApiResult Failure(string error, bool sessionInvalid = false, bool proxyFailed = false) =>
        new() { Ok = false, Error = error, SessionInvalid = sessionInvalid, ProxyFailed = proxyFailed };
}
