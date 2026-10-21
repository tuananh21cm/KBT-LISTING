using System.Globalization;
using Application.DTOs.ProductOpportunity;

namespace Application.Services;

/// <summary>
/// Builds TikTok <c>category_infos</c> for the extension scrape.
/// Seller Center treats one <c>category_infos</c> array as a single filter;
/// multiple shop catalog picks must be separate groups (OR), not concatenated.
/// </summary>
internal static class ProductOpportunityCategoryInfos
{
    /// <summary>One TikTokCategories row (selected catalog node).</summary>
    internal readonly record struct Node(string ExternalId, int Level);

    /// <summary>
    /// One group per catalog category: the selected node only
    /// (<c>level = TikTokCategory.Level + 1</c>). Duplicate ids collapsed;
    /// ids missing from <paramref name="byExternalId"/> skipped.
    /// </summary>
    public static List<List<ProductOpportunityCategoryInfoDto>> BuildGroups(
        IReadOnlyList<string> catalogIds,
        IReadOnlyDictionary<string, Node> byExternalId)
    {
        var groups = new List<List<ProductOpportunityCategoryInfoDto>>();
        if (catalogIds.Count == 0 || byExternalId.Count == 0)
        {
            return groups;
        }

        var seenCatalog = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in catalogIds)
        {
            var id = (raw ?? string.Empty).Trim();
            if (id.Length == 0
                || !seenCatalog.Add(id)
                || !long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cateId)
                || cateId <= 0
                || !byExternalId.TryGetValue(id, out var node))
            {
                continue;
            }

            groups.Add(
            [
                new ProductOpportunityCategoryInfoDto
                {
                    CateId = cateId,
                    Level = node.Level + 1
                }
            ]);
        }

        return groups;
    }
}
