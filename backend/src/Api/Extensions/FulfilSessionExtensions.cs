using Microsoft.AspNetCore.Http;

namespace Api.Extensions;

/// <summary>
/// Reads the fulfil-extension Chrome-profile session id from request headers.
/// </summary>
public static class FulfilSessionExtensions
{
    /// <summary>Header name sent by the fulfil extension on every API call.</summary>
    public const string HeaderName = "X-Fulfil-Session";

    /// <summary>
    /// Parses <see cref="HeaderName"/> as a Guid. Returns null when missing or invalid
    /// (list/GET endpoints still work without a session; claim endpoints require it).
    /// </summary>
    public static Guid? TryGetFulfilSessionId(this HttpContext http)
    {
        if (!http.Request.Headers.TryGetValue(HeaderName, out var raw))
        {
            return null;
        }

        var value = raw.ToString().Trim();
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Requires a valid session id — throws <see cref="Domain.Exceptions.BusinessException"/> (400) when missing.
    /// </summary>
    public static Guid GetFulfilSessionId(this HttpContext http)
    {
        var id = http.TryGetFulfilSessionId();
        if (id is null)
        {
            throw new Domain.Exceptions.BusinessException(
                $"Thiếu header {HeaderName} (định danh phiên Chrome profile).");
        }

        return id.Value;
    }
}
