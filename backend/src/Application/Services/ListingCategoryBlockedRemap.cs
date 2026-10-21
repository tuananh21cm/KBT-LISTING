namespace Application.Services;

/// <summary>
/// Parses the Settings map of TikTok category leaves that cannot be published into, and the sibling
/// leaf that should replace them.
/// </summary>
/// <remarks>
/// Pure string logic so the policy is unit-testable. Measured case: <c>903824</c> Costume Tops &amp;
/// Corsets fails Specifics publish for some shops with
/// <c>Cannot use custom value names … attribute ID 100401</c> even when the payload already carries
/// a numeric <c>valueId</c>; sibling <c>903952</c> Costume Sets accepts the same values.
/// </remarks>
public static class ListingCategoryBlockedRemap
{
    /// <summary>
    /// Reads <c>blockedId:replacementId</c> pairs (comma / semicolon / newline separated) into a
    /// case-insensitive map. Malformed entries are skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(string? raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return map;
        }

        foreach (var entry in raw.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sep = entry.IndexOf(':');
            if (sep <= 0 || sep >= entry.Length - 1)
            {
                continue;
            }

            var blocked = entry[..sep].Trim();
            var replacement = entry[(sep + 1)..].Trim();
            if (blocked.Length == 0
                || replacement.Length == 0
                || string.Equals(blocked, replacement, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            map[blocked] = replacement;
        }

        return map;
    }

    /// <summary>True when <paramref name="categoryId"/> is a blocked leaf in the map.</summary>
    public static bool IsBlocked(IReadOnlyDictionary<string, string> map, string? categoryId) =>
        !string.IsNullOrWhiteSpace(categoryId) && map.ContainsKey(categoryId.Trim());

    /// <summary>
    /// Resolves the replacement leaf for a blocked category id. Returns false when the id is not in
    /// the map or the replacement is empty.
    /// </summary>
    public static bool TryResolve(
        IReadOnlyDictionary<string, string> map,
        string? categoryId,
        out string replacementId)
    {
        replacementId = string.Empty;
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return false;
        }

        if (!map.TryGetValue(categoryId.Trim(), out var replacement)
            || string.IsNullOrWhiteSpace(replacement))
        {
            return false;
        }

        replacementId = replacement.Trim();
        return true;
    }

    /// <summary>Blocked leaf ids only — used by the guardrail to force Override.</summary>
    public static IReadOnlyList<string> BlockedIds(IReadOnlyDictionary<string, string> map) =>
        map.Keys.ToList();
}
