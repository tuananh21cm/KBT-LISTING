using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class ListingAddOriginApplyTests
{
    private static readonly Guid OwnerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OwnerB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("other", null)]
    [InlineData("Recommended", ListingAddedFrom.Recommended)]
    [InlineData("MANUAL", ListingAddedFrom.Manual)]
    [InlineData(" recommended ", ListingAddedFrom.Recommended)]
    public void Normalize_AcceptsKnownValues(string? raw, string? expected)
        => Assert.Equal(expected, ListingAddOriginApply.Normalize(raw));

    [Fact]
    public void Apply_SetsOriginWhenSourceUrlAndShopMatch()
    {
        var listing = NewListing(OwnerA, shopId: 11, url: SheinUrl("123"));
        ListingAddOriginApply.Apply(
            [listing],
            [Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Recommended)]);

        Assert.Equal(ListingAddedFrom.Recommended, listing.AddedFrom);
    }

    [Fact]
    public void Apply_SkipsWhenSourceUrlMissing()
    {
        var listing = NewListing(OwnerA, shopId: 11, url: null);
        ListingAddOriginApply.Apply(
            [listing],
            [Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Recommended)]);

        Assert.Null(listing.AddedFrom);
    }

    [Fact]
    public void Apply_MatchesShopNotSiblingShop()
    {
        var shop11 = NewListing(OwnerA, shopId: 11, url: SheinUrl("123"));
        var shop22 = NewListing(OwnerA, shopId: 22, url: SheinUrl("123"));
        ListingAddOriginApply.Apply(
            [shop11, shop22],
            [Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Recommended)]);

        Assert.Equal(ListingAddedFrom.Recommended, shop11.AddedFrom);
        Assert.Null(shop22.AddedFrom);
    }

    [Fact]
    public void Apply_WriteOnce_IgnoresLaterStaging()
    {
        var listing = NewListing(OwnerA, shopId: 11, url: SheinUrl("123"));
        listing.AddedFrom = ListingAddedFrom.Recommended;
        ListingAddOriginApply.Apply(
            [listing],
            [Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Manual)]);

        Assert.Equal(ListingAddedFrom.Recommended, listing.AddedFrom);
    }

    [Fact]
    public void Apply_DoesNotCrossOwners()
    {
        var listing = NewListing(OwnerB, shopId: 11, url: SheinUrl("123"));
        ListingAddOriginApply.Apply(
            [listing],
            [Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Recommended)]);

        Assert.Null(listing.AddedFrom);
    }

    [Fact]
    public void Apply_SkipsLegacyListingCreatedBeforeStaging()
    {
        var staged = DateTime.UtcNow;
        var listing = NewListing(OwnerA, shopId: 11, url: SheinUrl("123"));
        listing.CreatedAt = staged.AddDays(-30);
        var origin = Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Recommended);
        origin.CreatedAt = staged;

        ListingAddOriginApply.Apply([listing], [origin]);

        Assert.Null(listing.AddedFrom);
    }

    [Fact]
    public void Apply_UsesUpdatedAtAsStagedAt_LastWrite()
    {
        var listing = NewListing(OwnerA, shopId: 11, url: SheinUrl("123"));
        listing.CreatedAt = DateTime.UtcNow;
        var origin = Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Manual);
        origin.CreatedAt = DateTime.UtcNow.AddDays(-10);
        origin.UpdatedAt = DateTime.UtcNow;

        ListingAddOriginApply.Apply([listing], [origin]);

        Assert.Equal(ListingAddedFrom.Manual, listing.AddedFrom);
    }

    [Fact]
    public void Apply_SkipsWhenListingOlderThanLastWrite()
    {
        var listing = NewListing(OwnerA, shopId: 11, url: SheinUrl("123"));
        listing.CreatedAt = DateTime.UtcNow.AddDays(-2);
        var origin = Origin(OwnerA, SheinUrl("123"), 11, ListingAddedFrom.Recommended);
        origin.CreatedAt = DateTime.UtcNow.AddDays(-10);
        origin.UpdatedAt = DateTime.UtcNow;

        ListingAddOriginApply.Apply([listing], [origin]);

        Assert.Null(listing.AddedFrom);
    }

    private static Listing NewListing(Guid ownerId, long shopId, string? url) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ownerId,
        FourSellerShopId = shopId,
        SourceUrl = url
    };

    private static ListingAddOrigin Origin(Guid ownerId, string url, long shopId, string addedFrom) => new()
    {
        OwnerId = ownerId,
        SourceUrlKey = SourceUrlKeyHelper.Normalize(url),
        FourSellerShopId = shopId,
        AddedFrom = addedFrom
    };

    private static string SheinUrl(string goodsId) =>
        $"https://us.shein.com/item-p-{goodsId}.html?src_identifier=x";
}
