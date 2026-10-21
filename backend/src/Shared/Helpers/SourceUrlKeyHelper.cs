using System.Text.RegularExpressions;

namespace Shared.Helpers;

/// <summary>
/// Normalizes product source URLs for scrape draft matching.
/// </summary>
public static partial class SourceUrlKeyHelper
{
    /// <summary>
    /// Builds a stable key (Shein <c>-p-{id}</c> → <c>p:{id}</c>, else origin+path).
    /// </summary>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        try
        {
            var uri = new Uri(trimmed);
            var match = ProductIdRegex().Match(uri.AbsolutePath);
            if (match.Success)
            {
                return $"p:{match.Groups[1].Value}";
            }

            return (uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath).ToLowerInvariant();
        }
        catch (UriFormatException)
        {
            return trimmed.ToLowerInvariant();
        }
    }

    /// <summary>
    /// Canonical product URL for 4Seller crawl: origin + path only (strip query/fragment).
    /// Tracking params (e.g. Shein <c>?src_identifier=</c>) cause <c>data:[]</c> / delayed drafts.
    /// </summary>
    public static string CanonicalizeForCrawl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var trimmed = url.Trim();
        // Glued paste: "...htmlhttps://..." → keep only the first URL.
        var matches = HttpSchemeRegex().Matches(trimmed);
        if (matches.Count > 1)
        {
            trimmed = trimmed[..matches[1].Index];
        }

        try
        {
            var uri = new Uri(trimmed);
            if (uri.Scheme is not ("http" or "https"))
            {
                return string.Empty;
            }

            return uri.GetLeftPart(UriPartial.Path);
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts Shein-style product id from a URL path (<c>-p-{digits}</c>), else null.
    /// </summary>
    public static string? ExtractProductId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var uri = new Uri(url.Trim());
            var match = ProductIdRegex().Match(uri.AbsolutePath);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (UriFormatException)
        {
            var match = ProductIdRegex().Match(url);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    /// <summary>
    /// Resolves ProductSources code from a source URL host (SHEIN for shein.com).
    /// </summary>
    public static string? ResolveSourceCode(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var host = new Uri(url.Trim()).Host.ToLowerInvariant();
            if (host.Contains("shein", StringComparison.Ordinal))
            {
                return "SHEIN";
            }
        }
        catch (UriFormatException)
        {
            if (url.Contains("shein", StringComparison.OrdinalIgnoreCase))
            {
                return "SHEIN";
            }
        }

        return null;
    }

    [GeneratedRegex(@"-p-(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductIdRegex();

    [GeneratedRegex(@"https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpSchemeRegex();
}
