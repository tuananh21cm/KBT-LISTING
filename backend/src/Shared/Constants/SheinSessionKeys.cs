namespace Shared.Constants;

/// <summary>
/// SHEIN storefront session bits captured by the fulfil extension and replayed by the backend.
/// These headers are session-scoped (not signed per request), which is why the same set works
/// across pages and billnos.
/// </summary>
public static class SheinSessionKeys
{
    /// <summary>Headers without which SHEIN rejects an order API call.</summary>
    public static readonly string[] RequiredHeaders =
    [
        "armortoken",
        "smdeviceid",
        "x-csrf-token",
        "x-gw-auth"
    ];

    /// <summary>Storefront origin used when the account has none stored.</summary>
    public const string DefaultOrigin = "https://us.shein.com";

    /// <summary>Headers that must never be persisted — they are per-connection, content-only, or rebuilt per request.</summary>
    public static readonly HashSet<string> DeniedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "cookie",
        "host",
        "content-length",
        "content-type",
        "content-encoding",
        "content-language",
        "content-disposition",
        "content-location",
        "content-md5",
        "content-range",
        "allow",
        "expires",
        "last-modified",
        "connection",
        "keep-alive",
        "transfer-encoding",
        "upgrade",
        "proxy-authorization",
        "proxy-connection",
        "te",
        "trailer"
    };

    /// <summary>Cap on stored header count, so a runaway capture cannot bloat the row.</summary>
    public const int MaxHeaders = 40;

    /// <summary>Cap on a single stored header value.</summary>
    public const int MaxHeaderValueLength = 4000;

    /// <summary>Cap on the stored cookie header.</summary>
    public const int MaxCookieLength = 16000;

    /// <summary>Returns the required headers missing from a captured set.</summary>
    public static List<string> MissingRequiredHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return [.. RequiredHeaders];
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                present.Add(key.Trim());
            }
        }

        return RequiredHeaders.Where(h => !present.Contains(h)).ToList();
    }
}
