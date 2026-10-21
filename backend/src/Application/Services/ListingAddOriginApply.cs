using Domain.Entities;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Pure matching for <see cref="ListingAddOrigin"/> → <see cref="Listing.AddedFrom"/>.
/// </summary>
public static class ListingAddOriginApply
{
    /// <summary>
    /// Allow listing.CreatedAt slightly before staging (clock / 4Seller returning an existing draft).
    /// Listings older than this are treated as pre-existing and are not stamped.
    /// </summary>
    public static readonly TimeSpan CreatedAtGrace = TimeSpan.FromMinutes(5);

    /// <summary>Accepts <c>recommended</c> / <c>manual</c> (trim, case-insensitive); otherwise null.</summary>
    public static string? Normalize(string? addedFrom)
    {
        var s = (addedFrom ?? string.Empty).Trim().ToLowerInvariant();
        return s is ListingAddedFrom.Recommended or ListingAddedFrom.Manual ? s : null;
    }

    /// <summary>Lookup key owner + source URL key + 4Seller shop.</summary>
    public static string MatchKey(Guid ownerId, string sourceUrlKey, long fourSellerShopId) =>
        $"{ownerId:N}|{sourceUrlKey}|{fourSellerShopId}";

    /// <summary>Latest staging timestamp (last-write uses <see cref="ListingAddOrigin.UpdatedAt"/>).</summary>
    public static DateTime StagedAt(ListingAddOrigin origin) =>
        origin.UpdatedAt ?? origin.CreatedAt;

    /// <summary>
    /// Sets <see cref="Listing.AddedFrom"/> when empty, SourceUrl is present, a staging row matches,
    /// and the listing was created around/after that staging (not a legacy row).
    /// Does not overwrite an origin already on the listing (write-once).
    /// </summary>
    public static void Apply(
        IReadOnlyCollection<Listing> listings,
        IReadOnlyCollection<ListingAddOrigin> origins)
    {
        if (listings.Count == 0 || origins.Count == 0)
        {
            return;
        }

        var map = new Dictionary<string, ListingAddOrigin>(StringComparer.OrdinalIgnoreCase);
        foreach (var originRow in origins)
        {
            if (Normalize(originRow.AddedFrom) is null || string.IsNullOrWhiteSpace(originRow.SourceUrlKey))
            {
                continue;
            }

            map[MatchKey(originRow.OwnerId, originRow.SourceUrlKey, originRow.FourSellerShopId)] = originRow;
        }

        foreach (var listing in listings)
        {
            if (!string.IsNullOrWhiteSpace(listing.AddedFrom))
            {
                continue;
            }

            if (listing.FourSellerShopId is not > 0)
            {
                continue;
            }

            var key = SourceUrlKeyHelper.Normalize(listing.SourceUrl);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!map.TryGetValue(MatchKey(listing.OwnerId, key, listing.FourSellerShopId.Value), out var originRow))
            {
                continue;
            }

            if (listing.CreatedAt < StagedAt(originRow) - CreatedAtGrace)
            {
                continue;
            }

            listing.AddedFrom = Normalize(originRow.AddedFrom);
        }
    }
}
