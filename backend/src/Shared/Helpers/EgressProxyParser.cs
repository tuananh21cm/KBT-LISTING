using System.Net;

namespace Shared.Helpers;

/// <summary>
/// Parses the proxy strings operators paste into settings (4Seller sellers, SHEIN fulfil
/// accounts) into a <see cref="WebProxy"/>.
/// </summary>
public static class EgressProxyParser
{
    /// <summary>Default proxy scheme when the operator omits a <c>scheme://</c> prefix.</summary>
    public const string DefaultScheme = "http";

    /// <summary>
    /// Parses a proxy string. Accepted formats:
    /// <list type="bullet">
    /// <item><c>host:port</c> (IP-whitelisted, no auth)</item>
    /// <item><c>host:port:user:pass</c> (colon-delimited auth)</item>
    /// <item><c>[scheme://][user:pass@]host:port</c> (URL form)</item>
    /// </list>
    /// A leading <c>scheme://</c> (http/https/socks5/socks4/socks4a) may prefix any of the above;
    /// scheme defaults to <see cref="DefaultScheme"/> (HTTP CONNECT tunnel for HTTPS targets; prefix <c>https://</c> for HTTPS proxy).
    /// </summary>
    public static bool TryBuild(string? raw, out WebProxy proxy)
    {
        proxy = null!;
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var scheme = DefaultScheme;
        var schemeIdx = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx > 0)
        {
            scheme = value[..schemeIdx].Trim().ToLowerInvariant();
            value = value[(schemeIdx + 3)..];
        }

        string host;
        int port;
        string? user = null;
        string? pass = null;

        var atIdx = value.IndexOf('@');
        if (atIdx >= 0)
        {
            // user:pass@host:port
            var creds = value[..atIdx].Split(':', 2);
            user = Uri.UnescapeDataString(creds[0]);
            pass = creds.Length > 1 ? Uri.UnescapeDataString(creds[1]) : string.Empty;
            if (!TrySplitHostPort(value[(atIdx + 1)..], out host, out port))
            {
                return false;
            }
        }
        else
        {
            var parts = value.Split(':');
            switch (parts.Length)
            {
                case 2:
                    host = parts[0];
                    break;
                case 4:
                    // host:port:user:pass
                    host = parts[0];
                    user = parts[2];
                    pass = parts[3];
                    break;
                default:
                    return false;
            }

            if (!int.TryParse(parts[1], out port))
            {
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
        {
            return false;
        }

        Uri proxyUri;
        try
        {
            proxyUri = new UriBuilder(scheme, host, port).Uri;
        }
        catch
        {
            return false;
        }

        proxy = new WebProxy(proxyUri) { UseDefaultCredentials = false };
        if (!string.IsNullOrEmpty(user))
        {
            proxy.Credentials = new NetworkCredential(user, pass ?? string.Empty);
        }

        return true;
    }

    /// <summary>True when <paramref name="raw"/> includes an explicit <c>scheme://</c> prefix.</summary>
    public static bool HasExplicitScheme(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && raw.Trim().IndexOf("://", StringComparison.Ordinal) > 0;

    /// <summary>Returns the scheme from <paramref name="raw"/> or <see cref="DefaultScheme"/> when omitted.</summary>
    public static string GetSchemeOrDefault(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return DefaultScheme;
        }

        var schemeIdx = value.IndexOf("://", StringComparison.Ordinal);
        return schemeIdx > 0
            ? value[..schemeIdx].Trim().ToLowerInvariant()
            : DefaultScheme;
    }

    /// <summary>Replaces or adds a <c>scheme://</c> prefix while preserving host/port/credentials.</summary>
    public static string WithScheme(string raw, string scheme)
    {
        var value = raw.Trim();
        var schemeIdx = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx > 0)
        {
            value = value[(schemeIdx + 3)..];
        }

        return $"{scheme.Trim().ToLowerInvariant()}://{value}";
    }

    private static bool TrySplitHostPort(string hostPort, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        var idx = hostPort.LastIndexOf(':');
        if (idx <= 0 || idx >= hostPort.Length - 1)
        {
            return false;
        }

        host = hostPort[..idx];
        return int.TryParse(hostPort[(idx + 1)..], out port);
    }
}
