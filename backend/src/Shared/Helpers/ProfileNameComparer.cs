using System.Text.RegularExpressions;

namespace Shared.Helpers;

/// <summary>
/// Same profile-name ordering as admin Shops "Theo tên Profile" (asc):
/// split trailing digits, strip separators from prefix, compare prefix then numeric index.
/// Empty names always sort last.
/// </summary>
public sealed class ProfileNameComparer : IComparer<string?>
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly ProfileNameComparer Instance = new();

    private static readonly Regex TrailingDigits = new(
        @"^(.*?)(\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Separators = new(
        @"[\s_\-.]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        var left = Split(x);
        var right = Split(y);
        if (left.IsEmpty != right.IsEmpty)
        {
            return left.IsEmpty ? 1 : -1;
        }

        var byPrefix = string.Compare(left.Prefix, right.Prefix, StringComparison.OrdinalIgnoreCase);
        if (byPrefix != 0)
        {
            return byPrefix;
        }

        if (left.Index != right.Index)
        {
            if (left.Index is null)
            {
                return 1;
            }

            if (right.Index is null)
            {
                return -1;
            }

            return left.Index.Value.CompareTo(right.Index.Value);
        }

        return 0;
    }

    private static (string Prefix, int? Index, bool IsEmpty) Split(string? profileName)
    {
        var name = (profileName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return (string.Empty, null, true);
        }

        var match = TrailingDigits.Match(name);
        var rawPrefix = match.Success ? match.Groups[1].Value : name;
        var prefix = Separators.Replace(rawPrefix.ToLowerInvariant(), string.Empty);
        int? index = null;
        if (match.Success && int.TryParse(match.Groups[2].Value, out var n))
        {
            index = n;
        }

        return (prefix, index, false);
    }
}
