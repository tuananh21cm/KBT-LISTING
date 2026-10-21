using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingRecentQueryTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void NormalizePageType_DefaultsToDraft()
    {
        Assert.Equal(ListingPageTypes.Draft, ListingRecentQuery.NormalizePageType(null));
        Assert.Equal(ListingPageTypes.Draft, ListingRecentQuery.NormalizePageType(""));
        Assert.Equal(ListingPageTypes.Draft, ListingRecentQuery.NormalizePageType("DRAFT"));
        Assert.Equal(ListingPageTypes.Published, ListingRecentQuery.NormalizePageType("Published"));
    }

    [Fact]
    public void ParseFourSellerShopIds_SplitsAndDropsInvalid()
    {
        var ids = ListingRecentQuery.ParseFourSellerShopIds("11, 0, 22;22 abc, -3, 33");
        Assert.Equal([11L, 22L, 33L], ids);
        Assert.Empty(ListingRecentQuery.ParseFourSellerShopIds(null));
        Assert.Empty(ListingRecentQuery.ParseFourSellerShopIds("  "));
    }

    [Fact]
    public void NormalizePaging_ClampsPageAndSize()
    {
        Assert.Equal((1, 20, 0), ListingRecentQuery.NormalizePaging(0, 0));
        Assert.Equal((2, 20, 20), ListingRecentQuery.NormalizePaging(2, 20));
        Assert.Equal((3, 50, 100), ListingRecentQuery.NormalizePaging(3, 99));
    }

    [Fact]
    public void ApplyFilterAndPaging_ReturnsOnlyRequestedSliceAndTotal()
    {
        var rows = new List<Listing>();
        for (var i = 1; i <= 25; i++)
        {
            rows.Add(Draft(i, shopId: 100, created: new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc)));
        }

        rows.Add(Draft(90, shopId: 200, created: Utc(2026, 2, 1)));
        rows.Add(Published(91, shopId: 100, updated: Utc(2026, 3, 1)));
        rows.Add(Draft(92, shopId: 100, created: Utc(2026, 4, 1), ownerId: Other));

        var filtered = ListingRecentQuery.ApplyFilter(
            rows.AsQueryable(),
            Owner,
            ListingPageTypes.Draft,
            [100]);

        var total = filtered.Count();
        Assert.Equal(25, total);

        var (_, size, skip) = ListingRecentQuery.NormalizePaging(2, 20);
        var page = ListingRecentQuery.ApplySort(filtered, ListingPageTypes.Draft)
            .Skip(skip)
            .Take(size)
            .Select(l => l.FourSellerListingId)
            .ToList();

        Assert.Equal(5, page.Count);
        Assert.DoesNotContain(90, page);
        Assert.DoesNotContain(91, page);
        Assert.DoesNotContain(92, page);
        Assert.Equal(5, page.Max());
        Assert.Equal(1, page.Min());
    }

    [Fact]
    public void ApplyFilter_Published_OnlyPublishedForSelectedShops()
    {
        var rows = new List<Listing>
        {
            Published(1, 10, Utc(2026, 5, 2)),
            Published(2, 20, Utc(2026, 5, 3)),
            Published(3, 10, Utc(2026, 5, 1)),
            Draft(4, 10, Utc(2026, 5, 4))
        };

        var filtered = ListingRecentQuery.ApplyFilter(
            rows.AsQueryable(),
            Owner,
            ListingPageTypes.Published,
            [10, 20]);

        var page = ListingRecentQuery.ApplySort(filtered, ListingPageTypes.Published)
            .Select(l => l.FourSellerListingId)
            .ToList();

        Assert.Equal([2L, 1L, 3L], page);
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static Listing Draft(long listingId, long shopId, DateTime created, Guid? ownerId = null) => new()
    {
        Id = Guid.NewGuid(),
        FourSellerListingId = listingId,
        OwnerId = ownerId ?? Owner,
        FourSellerShopId = shopId,
        PageType = ListingPageTypes.Draft,
        ProductName = $"d-{listingId}",
        PlatformCreateTime = created,
        CreatedAt = created,
        LastSyncedAt = created
    };

    private static Listing Published(long listingId, long shopId, DateTime updated) => new()
    {
        Id = Guid.NewGuid(),
        FourSellerListingId = listingId,
        OwnerId = Owner,
        FourSellerShopId = shopId,
        PageType = ListingPageTypes.Published,
        SiteStatus = ListingSiteStatuses.Active,
        ProductName = $"p-{listingId}",
        PlatformUpdateTime = updated,
        LastSyncedAt = updated,
        CreatedAt = updated.AddDays(-1)
    };
}
