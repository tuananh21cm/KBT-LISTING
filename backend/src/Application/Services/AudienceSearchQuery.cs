using Application.DTOs.ProductOpportunity;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Builds a SHEIN / paste sourcing query from a TikTok trending keyword plus shop body/size.
/// Does not rewrite the displayed trend keyword (volume stays on the original phrase).
/// </summary>
public static class AudienceSearchQuery
{
    /// <summary>Lowercase display form: letters/digits, spaces, hyphen, apostrophe.</summary>
    public static string Normalize(string? raw)
        => ShopKeywordResearchSuggest.NormalizeDisplay(raw);

    /// <summary>
    /// Sourcing query: prefix a body-size modifier when the shop has one and the keyword
    /// does not already contain it or a conflicting size.
    /// </summary>
    public static string Build(string? keyword, string? bodySizeCode)
    {
        var query = Normalize(keyword);
        if (query.Length == 0)
        {
            return string.Empty;
        }

        var modifier = ModifierFor(bodySizeCode);
        if (modifier is null)
        {
            return query;
        }

        if (HasConflictingSize(query, bodySizeCode) || HasModifier(query, bodySizeCode))
        {
            return query;
        }

        return $"{modifier} {query}";
    }

    /// <summary>Organic SHEIN US pdsearch URL, or null when the query is empty.</summary>
    public static string? BuildSheinUrl(string? searchQuery)
    {
        var q = (searchQuery ?? string.Empty).Trim();
        return q.Length == 0 ? null : WhatToSellTodayService.BuildSheinSearchUrl(q);
    }

    /// <summary>
    /// True when <paramref name="keyword"/> already carries the required body-size token
    /// for plus / petite / tall shops (used by audience-suggest accept).
    /// </summary>
    public static bool HasRequiredBodyModifier(string? keyword, string? bodySizeCode)
    {
        var body = (bodySizeCode ?? string.Empty).Trim().ToLowerInvariant();
        if (body is not (ShopBodySizeCodes.Plus or ShopBodySizeCodes.Petite or ShopBodySizeCodes.Tall))
        {
            return true;
        }

        var query = Normalize(keyword);
        return query.Length > 0 && HasModifier(query, body);
    }

    /// <summary>Stamp <c>searchQuery</c> / <c>sheinUrl</c> from the live shop body/size.</summary>
    public static void Apply(ShopKeywordResearchDto dto, string? bodySizeCode)
    {
        ApplyList(dto.ListNow, bodySizeCode);
        ApplyList(dto.Upcoming, bodySizeCode);
    }

    private static void ApplyList(List<ShopKeywordResearchItemDto> items, string? bodySizeCode)
    {
        foreach (var item in items)
        {
            item.SearchQuery = Build(item.Keyword, bodySizeCode);
            item.SheinUrl = BuildSheinUrl(item.SearchQuery);
        }
    }

    private static string? ModifierFor(string? bodySizeCode)
        => (bodySizeCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ShopBodySizeCodes.Plus => "plus size",
            ShopBodySizeCodes.Petite => "petite",
            ShopBodySizeCodes.Tall => "tall",
            ShopBodySizeCodes.Curvy => "curvy",
            _ => null
        };

    private static bool HasModifier(string query, string? bodySizeCode)
        => (bodySizeCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ShopBodySizeCodes.Plus => HasPlusSize(query),
            ShopBodySizeCodes.Petite => HasToken(query, "petite"),
            ShopBodySizeCodes.Tall => HasToken(query, "tall"),
            ShopBodySizeCodes.Curvy => HasToken(query, "curvy"),
            _ => false
        };

    private static bool HasConflictingSize(string query, string? bodySizeCode)
        => (bodySizeCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ShopBodySizeCodes.Plus => HasToken(query, "petite") || HasToken(query, "tall"),
            ShopBodySizeCodes.Petite => HasPlusSize(query) || HasToken(query, "tall"),
            ShopBodySizeCodes.Tall => HasPlusSize(query) || HasToken(query, "petite"),
            ShopBodySizeCodes.Curvy => HasToken(query, "petite"),
            _ => false
        };

    private static bool HasPlusSize(string query)
    {
        var spaced = Spaced(query);
        return spaced.Contains("plus size", StringComparison.Ordinal);
    }

    private static bool HasToken(string query, string token)
    {
        foreach (var part in Spaced(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Spaced(string query)
    {
        var s = query.Replace('-', ' ');
        while (s.Contains("  ", StringComparison.Ordinal))
        {
            s = s.Replace("  ", " ", StringComparison.Ordinal);
        }

        return s;
    }
}
