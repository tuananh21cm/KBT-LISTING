namespace Application.Services;

/// <summary>
/// Immutable snapshot of the product attribute values TikTok answers with
/// <c>does not support custom value names</c>, learned from real publish failures.
/// <para>
/// 4Seller keeps listing such a value in <c>valueList</c> with a numeric id long after TikTok stops
/// honouring it. TikTok then drops the unknown id, reads the name as a custom value, and rejects the
/// whole publish — so the only reliable signal is the failure itself.
/// </para>
/// <para>
/// Keyed by attribute id plus normalized value name, and built per category, because one attribute id
/// carries a different enum per category (<c>100401</c> lists 6 values on category 903824 and more
/// elsewhere), so a value rejected in one category must not be blocked everywhere.
/// </para>
/// </summary>
public sealed class TikTokAttributeValueDenylist
{
    private readonly HashSet<string> _keys;

    /// <summary>Creates a snapshot from attribute id / value name pairs.</summary>
    public TikTokAttributeValueDenylist(IEnumerable<(string AttributeId, string ValueName)> entries)
    {
        _keys = entries
            .Select(e => BuildKey(e.AttributeId, e.ValueName))
            .Where(k => k.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Nothing rejected yet — the common case, so callers can skip filtering entirely.</summary>
    public static TikTokAttributeValueDenylist Empty { get; } = new([]);

    /// <summary>True when no value is blocked.</summary>
    public bool IsEmpty => _keys.Count == 0;

    /// <summary>Number of blocked values, for logging.</summary>
    public int Count => _keys.Count;

    /// <summary>True when TikTok has rejected this value name on this attribute.</summary>
    public bool IsRejected(string? attributeId, string? valueName)
    {
        if (_keys.Count == 0)
        {
            return false;
        }

        var key = BuildKey(attributeId, valueName);
        return key.Length > 0 && _keys.Contains(key);
    }

    private static string BuildKey(string? attributeId, string? valueName)
    {
        var attr = (attributeId ?? string.Empty).Trim();
        var value = ListingSpecificsMappingEngine.NormalizeKey(valueName);
        return attr.Length == 0 || value.Length == 0
            ? string.Empty
            : $"{attr}|{value}";
    }
}
