namespace Application.Services;

/// <summary>
/// Saved-keyword scope: bookmarks are per shop, not per seller.
/// Two shops of the same owner (often sharing a TikTok <c>lead_id</c>) keep independent saves.
/// </summary>
public static class ProductOpportunitySavedApply
{
    /// <summary>Trimmed lower-case keyword used as the uniqueness key.</summary>
    public static string Normalize(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// A bookmark appears in shop B's saved list only when it was saved on shop B.
    /// Presence of the same keyword in shop B's snapshot does not inherit shop A's save.
    /// </summary>
    public static bool BelongsToShop(Guid? savedShopId, Guid shopId) =>
        savedShopId.HasValue && savedShopId.Value == shopId;

    /// <summary>
    /// After unlinking a keyword from a shop, drop that shop's bookmark only.
    /// Other shops of the same owner keep theirs.
    /// </summary>
    public static bool ShouldDropBookmark(
        Guid savedShopId,
        string keywordNormalized,
        IReadOnlySet<(Guid ShopId, string Key)> remainingByShop) =>
        !remainingByShop.Contains((savedShopId, keywordNormalized));

    /// <summary>True when any of the row's scoped shops has this keyword bookmarked.</summary>
    public static bool IsSavedForShops(
        IReadOnlySet<(Guid ShopId, string Keyword)> savedKeys,
        IReadOnlyList<Guid> shopIds,
        string leadName)
    {
        var key = Normalize(leadName);
        if (key.Length == 0 || shopIds.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < shopIds.Count; i++)
        {
            if (savedKeys.Contains((shopIds[i], key)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bookmarks whose keyword is not in this shop's TikTok snapshot.
    /// They still belong on the shop list — extension Search reads the same table.
    /// </summary>
    public static List<T> WithoutSnapshot<T>(
        IReadOnlyList<T> saved,
        Func<T, string> normalizedKeyword,
        IReadOnlySet<string> snapshotNormalized)
    {
        if (saved.Count == 0)
        {
            return [];
        }

        var kept = new List<T>(saved.Count);
        foreach (var row in saved)
        {
            var key = normalizedKeyword(row);
            if (key.Length == 0 || snapshotNormalized.Contains(key))
            {
                continue;
            }

            kept.Add(row);
        }

        return kept;
    }
}
