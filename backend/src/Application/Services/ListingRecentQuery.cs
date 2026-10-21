using Domain.Entities;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Filter / sort / paging helpers for extension <c>GET /listings/drafts</c>.
/// Keeps Skip/Take on <see cref="IQueryable{T}"/> so paging happens in the DB.
/// </summary>
public static class ListingRecentQuery
{
    /// <summary>Default page size for the extension list.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Hard cap on page size.</summary>
    public const int MaxPageSize = 50;

    /// <summary><c>published</c> or default <c>draft</c>.</summary>
    public static string NormalizePageType(string? pageType)
    {
        var pt = (pageType ?? string.Empty).Trim().ToLowerInvariant();
        return pt == ListingPageTypes.Published
            ? ListingPageTypes.Published
            : ListingPageTypes.Draft;
    }

    /// <summary>Parse csv / whitespace shop ids; drop values ≤ 0.</summary>
    public static IReadOnlyList<long> ParseFourSellerShopIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        var ids = new List<long>();
        foreach (var part in csv.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part.Trim(), out var id) && id > 0 && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Clamp pageCurrent (≥1) and pageSize (1–50, default 20).</summary>
    public static (int PageCurrent, int PageSize, int Skip) NormalizePaging(
        int pageCurrent,
        int pageSize)
    {
        var page = pageCurrent < 1 ? 1 : pageCurrent;
        var size = pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        return (page, size, (page - 1) * size);
    }

    /// <summary>Owner + pageType + optional 4Seller shop ids.</summary>
    public static IQueryable<Listing> ApplyFilter(
        IQueryable<Listing> query,
        Guid ownerId,
        string pageType,
        IReadOnlyList<long>? fourSellerShopIds)
    {
        var q = query.Where(l => l.OwnerId == ownerId && l.PageType == pageType);
        if (fourSellerShopIds is { Count: > 0 })
        {
            var ids = fourSellerShopIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count > 0)
            {
                q = q.Where(l => l.FourSellerShopId != null && ids.Contains(l.FourSellerShopId.Value));
            }
        }

        return q;
    }

    /// <summary>
    /// Draft: create time. Published: update / last sync (same idea as admin list).
    /// </summary>
    public static IOrderedQueryable<Listing> ApplySort(IQueryable<Listing> query, string pageType)
    {
        if (pageType == ListingPageTypes.Published)
        {
            return query
                .OrderByDescending(l => l.PlatformUpdateTime ?? l.LastSyncedAt)
                .ThenByDescending(l => l.CreatedAt);
        }

        return query
            .OrderByDescending(l => l.PlatformCreateTime ?? l.CreatedAt)
            .ThenByDescending(l => l.LastSyncedAt);
    }
}
