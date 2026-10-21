using System.Globalization;
using System.Text.RegularExpressions;

namespace Shared.Helpers;

/// <summary>
/// Parse / normalize supplier PDP metrics captured from source realtime JSON
/// (Shein <c>get_goods_detail_realtime_data</c>).
/// </summary>
public static partial class SupplierPdpMetrics
{
    /// <summary>Canonical Shein US store home URL.</summary>
    public const string SheinUsStoreHome = "https://us.shein.com/store/home";

    /// <summary>Builds a stable store URL for a source platform + store code.</summary>
    public static string? BuildStoreUrl(string? platform, string? storeCode, string? providedUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(providedUrl))
        {
            var trimmed = providedUrl.Trim();
            return trimmed.Length > 500 ? trimmed[..500] : trimmed;
        }

        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return null;
        }

        var code = storeCode.Trim();
        if (IsShein(platform))
        {
            return $"{SheinUsStoreHome}?store_code={Uri.EscapeDataString(code)}";
        }

        return null;
    }

    /// <summary>True when platform code / name refers to Shein.</summary>
    public static bool IsShein(string? platform)
        => !string.IsNullOrWhiteSpace(platform)
           && platform.Contains("shein", StringComparison.OrdinalIgnoreCase);

    /// <summary>Normalizes ProductSource.Code (trim, upper).</summary>
    public static string? NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var t = platform.Trim().ToUpperInvariant();
        return t.Length > 32 ? t[..32] : t;
    }

    /// <summary>
    /// Extracts percent from Shein SLA HTML/text
    /// (prefer <c>dayPercentDesc</c>, e.g. <c>88% are ≤ 7 business days</c>).
    /// </summary>
    public static decimal? ParseShippingDayPercent(string? desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
        {
            return null;
        }

        var match = PercentRegex().Match(desc);
        if (!match.Success)
        {
            return null;
        }

        if (!decimal.TryParse(
                match.Groups[1].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return null;
        }

        if (value < 0 || value > 100)
        {
            return null;
        }

        return Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Extracts SLA days from <c>dayPercentDesc</c>, e.g. <c>≤ &lt;b&gt;7&lt;/b&gt; business days</c>.
    /// </summary>
    public static int? ParseShippingDaysFromDesc(string? desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
        {
            return null;
        }

        var match = SlaDaysRegex().Match(desc);
        if (!match.Success)
        {
            match = BusinessDaysRegex().Match(desc);
        }

        if (!match.Success
            || !int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var days)
            || days < 0)
        {
            return null;
        }

        return days;
    }

    /// <summary>
    /// Sums Shein <c>dayPercents</c> buckets except overflow (<c>&gt;N business days</c>).
    /// Example: 22+18+29+10+9 = 88 (skips <c>&gt;7</c> = 12).
    /// </summary>
    public static decimal? SumOnTimeDayPercents(IEnumerable<(string? Day, string? Percent)> buckets)
    {
        decimal sum = 0;
        var any = false;
        foreach (var (day, percent) in buckets)
        {
            var label = day?.Trim() ?? string.Empty;
            if (label.StartsWith('>'))
            {
                continue;
            }

            if (!decimal.TryParse(
                    percent,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                continue;
            }

            sum += value;
            any = true;
        }

        if (!any)
        {
            return null;
        }

        var rounded = Math.Round(sum, 2, MidpointRounding.AwayFromZero);
        return rounded is >= 0 and <= 100 ? rounded : null;
    }

    /// <summary>Non-negative stock, else null.</summary>
    public static int? NormalizeStock(int? value)
    {
        if (value is null || value < 0)
        {
            return null;
        }

        return value;
    }

    /// <summary>Non-negative review count, else null.</summary>
    public static int? NormalizeReviewCount(int? value)
    {
        if (value is null || value < 0)
        {
            return null;
        }

        return value;
    }

    /// <summary>Rating 0–5 rounded to 2 decimals, else null.</summary>
    public static decimal? NormalizeRating(decimal? value)
    {
        if (value is null || value < 0 || value > 5)
        {
            return null;
        }

        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Truthy PDP flag: <c>true</c> / <c>1</c> / <c>"1"</c> / <c>"true"</c>.
    /// SKU-level Shein <c>isSupportQuickShip</c> is often the string <c>"0"</c> — that is false.
    /// </summary>
    public static bool IsTruthyFlag(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            byte n => n == 1,
            short n => n == 1,
            int n => n == 1,
            long n => n == 1,
            string s => s.Trim().Equals("1", StringComparison.OrdinalIgnoreCase)
                        || s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
            _ => IsTruthyFlag(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    /// <summary>
    /// Local US stock: <c>localMadeTips</c> equals <c>Local</c> (case-insensitive),
    /// else fallback <c>productInfo.isLocalProduct</c>.
    /// </summary>
    public static bool? ParseIsLocalProduct(string? localMadeTips, object? isLocalProduct)
    {
        if (!string.IsNullOrWhiteSpace(localMadeTips)
            && localMadeTips.Trim().Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (isLocalProduct is not null
            && !(isLocalProduct is string blank && string.IsNullOrWhiteSpace(blank)))
        {
            return IsTruthyFlag(isLocalProduct);
        }

        return string.IsNullOrWhiteSpace(localMadeTips) ? null : false;
    }

    /// <summary>
    /// Product-level quick ship from <c>mall_stock.isSupportQuickShip</c> only —
    /// never SKU-level <c>sku.isSupportQuickShip</c>.
    /// Prefers rows whose <c>mall_code</c> matches <paramref name="selectedMallCode"/>;
    /// if none match, ORs every mall.
    /// </summary>
    public static bool? AggregateMallQuickShip(
        IEnumerable<(string? MallCode, object? IsSupportQuickShip)> mallStocks,
        string? selectedMallCode)
    {
        var list = mallStocks as IList<(string? MallCode, object? IsSupportQuickShip)>
                   ?? mallStocks.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var selected = selectedMallCode?.Trim();
        IEnumerable<(string? MallCode, object? IsSupportQuickShip)> scope = list;
        if (!string.IsNullOrEmpty(selected))
        {
            var matched = list
                .Where(m => string.Equals(m.MallCode?.Trim(), selected, StringComparison.Ordinal))
                .ToList();
            if (matched.Count > 0)
            {
                scope = matched;
            }
        }

        return scope.Any(m => IsTruthyFlag(m.IsSupportQuickShip));
    }

    /// <summary>
    /// Parses shop name from Shein <c>soldByTips</c>, e.g. <c>Sold by kcruab-kp</c> → <c>kcruab-kp</c>.
    /// </summary>
    public static string? ParseStoreTitleFromSoldByTips(string? soldByTips)
    {
        if (string.IsNullOrWhiteSpace(soldByTips))
        {
            return null;
        }

        var match = SoldByTipsRegex().Match(soldByTips.Trim());
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups[1].Value.Trim().Trim('"', '\'', '“', '”');
        return Truncate(name, 500);
    }

    /// <summary>Prefers <paramref name="storeTitle"/>, else name parsed from <paramref name="soldByTips"/>.</summary>
    public static string? ResolveStoreTitle(string? storeTitle, string? soldByTips)
        => Truncate(storeTitle, 500) ?? ParseStoreTitleFromSoldByTips(soldByTips);

    /// <summary>Non-negative shop rank (not product rating 0–5).</summary>
    public static decimal? NormalizeShopRank(decimal? value)
    {
        if (value is null || value < 0)
        {
            return null;
        }

        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Non-negative follower count, else null.</summary>
    public static int? NormalizeStoreAttention(int? value)
    {
        if (value is null || value < 0)
        {
            return null;
        }

        return value;
    }

    /// <summary>Positive shipping days, else null.</summary>
    public static int? NormalizeShippingDays(int? value)
    {
        if (value is null || value < 0)
        {
            return null;
        }

        return value;
    }

    /// <summary>Makes protocol-relative image URLs absolute (<c>//cdn</c> → https).</summary>
    public static string? NormalizeAbsoluteUrl(string? value, int maxLen = 2000)
    {
        var t = Truncate(value, maxLen);
        if (t is null)
        {
            return null;
        }

        if (t.StartsWith("//", StringComparison.Ordinal))
        {
            t = "https:" + t;
        }

        return t.Length > maxLen ? t[..maxLen] : t;
    }

    /// <summary>Truncates URL / tip text.</summary>
    public static string? Truncate(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length > maxLen ? t[..maxLen] : t;
    }

    /// <summary>Infers ProductSource.Code from URL host when options / client platform are missing.</summary>
    public static string? InferPlatformFromUrl(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || !Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host.Contains("shein", StringComparison.Ordinal))
        {
            return "SHEIN";
        }

        if (host.Contains("temu", StringComparison.Ordinal))
        {
            return "TEMU";
        }

        if (host.Contains("cjdropshipping", StringComparison.Ordinal) || host.Contains("cj.com", StringComparison.Ordinal))
        {
            return "CJ";
        }

        return null;
    }

    /// <summary>Resolves ProductSource.Code from a source URL host vs configured domains.</summary>
    public static string? MatchPlatformFromDomains(
        string? sourceUrl,
        IEnumerable<(string Code, string? Domains)> sources)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl) || !Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host.Trim().TrimStart('.').ToLowerInvariant();
        foreach (var (code, domains) in sources)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(domains))
            {
                continue;
            }

            foreach (var raw in domains.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var d = raw.Trim().TrimStart('.').ToLowerInvariant();
                if (d.Length == 0)
                {
                    continue;
                }

                if (host == d || host.EndsWith('.' + d, StringComparison.Ordinal))
                {
                    return NormalizePlatform(code);
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"[≤<=]\s*(?:<[^>]*>\s*)*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex SlaDaysRegex();

    [GeneratedRegex(@"(\d+)\s*(?:</b>)?\s*business\s+days", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BusinessDaysRegex();

    [GeneratedRegex(@"sold\s+by\s*[:\-]?\s*(.+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SoldByTipsRegex();
}
