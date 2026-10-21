using Application.DTOs.FourSeller;
using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;
namespace Application.Tests;

public class ListingPublishStatusSyncTests
{
    [Theory]
    [InlineData("publishing", PublishStatuses.Publishing)]
    [InlineData("Publishing", PublishStatuses.Publishing)]
    [InlineData("underreview", PublishStatuses.UnderReview)]
    [InlineData("UnderReview", PublishStatuses.UnderReview)]
    [InlineData("Failed", PublishStatuses.Failed)]
    public void PublishStatuses_Normalize_MapsKnownValues(string raw, string expected)
    {
        Assert.Equal(expected, PublishStatuses.Normalize(raw));
    }

    [Theory]
    [InlineData(PublishStatuses.Publishing, true)]
    [InlineData("publishing", true)]
    [InlineData(PublishStatuses.UnderReview, true)]
    [InlineData("underreview", true)]
    [InlineData(PublishStatuses.Published, false)]
    [InlineData(PublishStatuses.Failed, false)]
    [InlineData(null, false)]
    public void PublishStatuses_IsInFlight_DetectsInFlightStatuses(string? status, bool expected)
    {
        Assert.Equal(expected, PublishStatuses.IsInFlight(status));
    }

    [Theory]
    [InlineData(PublishStatuses.Publishing, true)]
    [InlineData("publishing", true)]
    [InlineData("PUBLISHING", true)]
    [InlineData(PublishStatuses.UnderReview, true)]
    [InlineData("underreview", true)]
    [InlineData(PublishStatuses.Published, false)]
    [InlineData(PublishStatuses.Failed, false)]
    [InlineData(PublishStatuses.Draft, false)]
    [InlineData(null, false)]
    public void PublishStatuses_InFlightLowercase_MatchesIsInFlight(string? status, bool expected)
    {
        var inFlight = status != null
            && PublishStatuses.InFlightLowercase.Contains(status.ToLowerInvariant());
        Assert.Equal(expected, inFlight);
        Assert.Equal(expected, PublishStatuses.IsInFlight(status));
    }

    [Fact]
    public void ResolveListingStatuses_PublishingOnDraftBucket_MapsToPublishedPending()
    {
        var (pageType, siteStatus) = ListingSyncService.ResolveListingStatuses(
            ListingSiteStatuses.Draft,
            FourSellerProductStatuses.Draft,
            erpStatus: null,
            publishStatus: "publishing");

        Assert.Equal(ListingPageTypes.Published, pageType);
        Assert.Equal(ListingSiteStatuses.Pending, siteStatus);
    }

    [Fact]
    public void ResolveListingStatuses_PublishingOnActiveListing_KeepsPublishedActive()
    {
        var (pageType, siteStatus) = ListingSyncService.ResolveListingStatuses(
            ListingSiteStatuses.Active,
            FourSellerProductStatuses.Active,
            erpStatus: ListingSiteStatuses.Active,
            publishStatus: "publishing");

        Assert.Equal(ListingPageTypes.Published, pageType);
        Assert.Equal(ListingSiteStatuses.Active, siteStatus);
    }

    [Fact]
    public void ResolveListingStatuses_Reviewing_StillMapsToPublishedPending()
    {
        var (pageType, siteStatus) = ListingSyncService.ResolveListingStatuses(
            ListingSiteStatuses.Draft,
            FourSellerProductStatuses.Reviewing,
            erpStatus: null,
            publishStatus: "publishing");

        Assert.Equal(ListingPageTypes.Published, pageType);
        Assert.Equal(ListingSiteStatuses.Pending, siteStatus);
    }

    [Fact]
    public void ResolvePublishStatusForActiveListing_ClearsInFlightPublishing()
    {
        var record = new Application.DTOs.FourSeller.FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Active,
            PublishStatus = "publishing",
        };

        var result = ListingSyncService.ResolvePublishStatusForActiveListing(
            record,
            PublishStatuses.Publishing);

        Assert.Equal("Active", result);
    }

    [Theory]
    [InlineData("full", null, ListingSyncScopes.Full)]
    [InlineData("status", null, ListingSyncScopes.Status)]
    [InlineData(null, "light", ListingSyncScopes.Status)]
    [InlineData(null, "full", ListingSyncScopes.Full)]
    public void ListingSyncScopes_Resolve_MapsWireValues(string? syncScope, string? syncMode, string expected)
    {
        Assert.Equal(expected, ListingSyncScopes.Resolve(syncScope, syncMode));
    }

    [Fact]
    public void ApplyStatusRecord_DoesNotOverwriteContentFields()
    {
        var ownerId = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = "tiktok",
            ProductName = "Local title",
            DescriptionHtml = "<p>local description</p>",
            RawPayload = "{\"local\":true}",
            MainImage = "https://local.example/main.jpg",
            PublishStatus = PublishStatuses.Publishing,
            SiteStatus = ListingSiteStatuses.Pending,
            PageType = ListingPageTypes.Published,
        };

        var record = new FourSellerListingRecordDto
        {
            ProductName = "Remote title",
            DescriptionHtml = "<p>remote description</p>",
            ProductStatus = FourSellerProductStatuses.Active,
            PublishStatus = "publishing",
            ErpStatus = ListingSiteStatuses.Active,
            MarketplaceProductId = "tt-123",
            UpdateTime = "2026-08-25 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            ownerId,
            "tiktok",
            ListingPageTypes.Published,
            ListingSiteStatuses.Active,
            localShopId: null);

        Assert.Equal("Local title", listing.ProductName);
        Assert.Equal("<p>local description</p>", listing.DescriptionHtml);
        Assert.Equal("{\"local\":true}", listing.RawPayload);
        Assert.Equal("https://local.example/main.jpg", listing.MainImage);
        Assert.Equal(ListingSiteStatuses.Active, listing.SiteStatus);
        Assert.Equal("tt-123", listing.TikTokProductId);
        Assert.Equal("Active", listing.PublishStatus);
    }

    [Fact]
    public void ResolveEffectiveStatuses_ActiveProduct_ClearsPublishingForSkipCheck()
    {
        var listing = new Listing
        {
            PublishStatus = PublishStatuses.Publishing,
            LastPushAt = DateTime.UtcNow.AddMinutes(-5),
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Active,
            PublishStatus = "publishing",
            ErpStatus = ListingSiteStatuses.Active,
        };

        var (_, _, publishStatus) = ListingSyncService.ResolveEffectiveStatuses(
            listing,
            record,
            ListingPageTypes.Published,
            ListingSiteStatuses.Active);

        Assert.Equal("Active", publishStatus);
    }

    [Fact]
    public void ListingSyncRequest_ApplyDefaults_DraftOnly_UsesFullScope()
    {
        var request = new Application.DTOs.Listing.ListingSyncRequest
        {
            Statuses = [ListingSiteStatuses.Draft],
        };

        request.ApplyDefaults();

        Assert.Equal(ListingSyncScopes.Full, request.SyncScope);
    }

    [Theory]
    [InlineData(PublishStatuses.Publishing, true)]
    [InlineData(PublishStatuses.UnderReview, true)]
    [InlineData(PublishStatuses.Failed, true)]
    [InlineData("failed", true)]
    [InlineData(PublishStatuses.Published, false)]
    [InlineData(PublishStatuses.Draft, false)]
    [InlineData(null, false)]
    public void PublishStatuses_BlocksAutoPublishPick(string? status, bool expected) =>
        Assert.Equal(expected, PublishStatuses.BlocksAutoPublishPick(status));

    [Fact]
    public void ApplyStatusRecord_RawIncompleteDraft_DoesNotMarkFailed()
    {
        var ownerId = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = "tiktok",
            PageType = ListingPageTypes.Draft,
            SiteStatus = ListingSiteStatuses.Draft,
            ProductName = "Raw draft",
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Draft,
            PublishStatus = "unpublishable",
            ErpStatus = ListingSiteStatuses.Draft,
            FailedMessage = "Weight with Package;Product Dimensions",
            UpdateTime = "2026-09-04 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            ownerId,
            "tiktok",
            ListingPageTypes.Draft,
            ListingSiteStatuses.Draft,
            localShopId: null);

        Assert.Equal(ListingPageTypes.Draft, listing.PageType);
        Assert.Equal(PublishStatuses.Draft, listing.PublishStatus);
        Assert.Equal("Weight with Package;Product Dimensions", listing.SyncError);
        Assert.False(listing.AutoPublishSkipped);
    }

    [Fact]
    public void ApplyStatusRecord_HealsStaleFailedOnRawDraft()
    {
        var ownerId = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = "tiktok",
            PageType = ListingPageTypes.Draft,
            SiteStatus = ListingSiteStatuses.Draft,
            PublishStatus = PublishStatuses.Failed,
            SyncError = "Weight with Package;Product Dimensions",
            ProductName = "Stale failed",
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Draft,
            PublishStatus = "unpublishable",
            ErpStatus = ListingSiteStatuses.Draft,
            FailedMessage = "Weight with Package;Product Dimensions",
            UpdateTime = "2026-09-04 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            ownerId,
            "tiktok",
            ListingPageTypes.Draft,
            ListingSiteStatuses.Draft,
            localShopId: null);

        Assert.Equal(PublishStatuses.Draft, listing.PublishStatus);
        Assert.Equal("Weight with Package;Product Dimensions", listing.SyncError);
        Assert.False(listing.AutoPublishSkipped);
    }

    [Fact]
    public void ResolveEffectiveStatuses_RawIncompleteDraft_StaysDraft()
    {
        var listing = new Listing
        {
            PublishStatus = PublishStatuses.Failed,
            SiteStatus = ListingSiteStatuses.Draft,
            PageType = ListingPageTypes.Draft,
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Draft,
            PublishStatus = "unpublishable",
            ErpStatus = ListingSiteStatuses.Draft,
            FailedMessage = "Weight with Package;Product Dimensions",
        };

        var (_, _, publishStatus) = ListingSyncService.ResolveEffectiveStatuses(
            listing,
            record,
            ListingPageTypes.Draft,
            ListingSiteStatuses.Draft);

        Assert.Equal(PublishStatuses.Draft, publishStatus);
    }

    [Fact]
    public void ApplyStatusRecord_SilentDemoteAfterAutoPublish_SetsAutoPublishSkipped()
    {
        var ownerId = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = "tiktok",
            PageType = ListingPageTypes.Published,
            SiteStatus = ListingSiteStatuses.Pending,
            PublishStatus = PublishStatuses.Publishing,
            PublishSource = ListingPublishSources.Auto,
            LastPushAt = DateTime.UtcNow.AddHours(-2),
            ProductName = "Local",
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Draft,
            PublishStatus = "unpublishable",
            ErpStatus = ListingSiteStatuses.Draft,
            FailedMessage = null,
            UpdateTime = "2026-09-02 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            ownerId,
            "tiktok",
            ListingPageTypes.Draft,
            ListingSiteStatuses.Draft,
            localShopId: null);

        Assert.Equal(ListingPageTypes.Draft, listing.PageType);
        Assert.Equal(PublishStatuses.Failed, listing.PublishStatus);
        Assert.True(listing.AutoPublishSkipped);
        Assert.False(string.IsNullOrWhiteSpace(listing.AutoPublishSkipReason));
    }

    [Fact]
    public void ApplyStatusRecord_SilentDemote_DoesNotSkipManualPublish()
    {
        var ownerId = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = "tiktok",
            PageType = ListingPageTypes.Published,
            SiteStatus = ListingSiteStatuses.Pending,
            PublishStatus = PublishStatuses.Publishing,
            PublishSource = ListingPublishSources.Manual,
            LastPushAt = DateTime.UtcNow.AddHours(-2),
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Draft,
            PublishStatus = "unpublishable",
            ErpStatus = ListingSiteStatuses.Draft,
            UpdateTime = "2026-09-02 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            ownerId,
            "tiktok",
            ListingPageTypes.Draft,
            ListingSiteStatuses.Draft,
            localShopId: null);

        Assert.False(listing.AutoPublishSkipped);
    }

    [Fact]
    public void ApplyStatusRecord_HoldOptimisticPending_DoesNotSkipYet()
    {
        var ownerId = Guid.NewGuid();
        var listing = new Listing
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Platform = "tiktok",
            PageType = ListingPageTypes.Published,
            SiteStatus = ListingSiteStatuses.Pending,
            PublishStatus = PublishStatuses.Publishing,
            PublishSource = ListingPublishSources.Auto,
            LastPushAt = DateTime.UtcNow.AddMinutes(-5),
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Draft,
            PublishStatus = null,
            ErpStatus = ListingSiteStatuses.Draft,
            UpdateTime = "2026-09-02 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            ownerId,
            "tiktok",
            ListingPageTypes.Draft,
            ListingSiteStatuses.Draft,
            localShopId: null);

        Assert.Equal(ListingPageTypes.Published, listing.PageType);
        Assert.Equal(ListingSiteStatuses.Pending, listing.SiteStatus);
        Assert.False(listing.AutoPublishSkipped);
    }

    [Fact]
    public void SkipStatusBlockReason_OwnerMismatchOnMyShop_ForcesApply()
    {
        var syncer = Guid.NewGuid();
        var previous = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var updateTime = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc);

        var listing = new Listing
        {
            OwnerId = previous,
            ShopId = shopId,
            PlatformUpdateTime = updateTime,
            PageType = ListingPageTypes.Published,
            SiteStatus = ListingSiteStatuses.Active,
            PublishStatus = "Active",
            ErpStatus = ListingSiteStatuses.Active,
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Active,
            PublishStatus = "Active",
            ErpStatus = ListingSiteStatuses.Active,
            UpdateTime = "2026-09-04 10:00:00",
        };

        var reason = ListingSyncService.SkipStatusBlockReason(
            listing,
            record,
            syncer,
            ListingPageTypes.Published,
            ListingSiteStatuses.Active,
            localShopId: shopId,
            myShopIds: [shopId]);

        Assert.Equal("scope-owner", reason);
    }

    [Fact]
    public void ApplyStatusRecord_DoesNotStealOwnerWhenShopNotMine()
    {
        var syncer = Guid.NewGuid();
        var realOwner = Guid.NewGuid();
        var foreignShopId = Guid.NewGuid();
        var myShopId = Guid.NewGuid();

        var listing = new Listing
        {
            OwnerId = realOwner,
            ShopId = foreignShopId,
            ProductName = "Keep",
            PageType = ListingPageTypes.Published,
            SiteStatus = ListingSiteStatuses.Active,
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Active,
            PublishStatus = "Active",
            ErpStatus = ListingSiteStatuses.Active,
            MarketplaceProductId = "tt-keep",
            UpdateTime = "2026-09-04 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            syncer,
            "tiktok",
            ListingPageTypes.Published,
            ListingSiteStatuses.Active,
            localShopId: null,
            myShopIds: [myShopId]);

        Assert.Equal(realOwner, listing.OwnerId);
        Assert.Equal(foreignShopId, listing.ShopId);
        Assert.Equal("tt-keep", listing.TikTokProductId);
    }

    [Fact]
    public void ApplyStatusRecord_ReclaimsOwnerWhenShopIsMine()
    {
        var syncer = Guid.NewGuid();
        var previous = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        var listing = new Listing
        {
            OwnerId = previous,
            ShopId = shopId,
            PageType = ListingPageTypes.Published,
            SiteStatus = ListingSiteStatuses.Active,
        };

        var record = new FourSellerListingRecordDto
        {
            ProductStatus = FourSellerProductStatuses.Active,
            PublishStatus = "Active",
            ErpStatus = ListingSiteStatuses.Active,
            UpdateTime = "2026-09-04 10:00:00",
        };

        ListingSyncService.ApplyStatusRecord(
            listing,
            record,
            syncer,
            "tiktok",
            ListingPageTypes.Published,
            ListingSiteStatuses.Active,
            localShopId: shopId,
            myShopIds: [shopId]);

        Assert.Equal(syncer, listing.OwnerId);
        Assert.Equal(shopId, listing.ShopId);
    }

    [Fact]
    public void CanStampListingOwner_RequiresShopInMyMapWhenLocalShopMissing()
    {
        var syncer = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var listing = new Listing { OwnerId = Guid.NewGuid(), ShopId = shopId };

        Assert.False(ListingSyncService.CanStampListingOwner(
            listing, syncer, localShopId: null, myShopIds: [Guid.NewGuid()]));
        Assert.True(ListingSyncService.CanStampListingOwner(
            listing, syncer, localShopId: null, myShopIds: [shopId]));
        Assert.True(ListingSyncService.CanStampListingOwner(
            listing, syncer, localShopId: shopId, myShopIds: [Guid.NewGuid()]));
    }

    [Fact]
    public void SelectAdoptionCandidate_PicksRichMirrorMintedUnderThePreviousFourSellerAccount()
    {
        var owner = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        const string ttId = "1732597616983053116";

        var rich = new Listing
        {
            OwnerId = owner,
            ShopId = shopId,
            FourSellerListingId = 855249580831,
            FourSellerShopId = 9001,
            TikTokProductId = ttId,
            SourceUrl = "https://us.shein.com/dress.html",
            RawPayload = "{\"ok\":true}"
        };

        var match = ListingSyncService.SelectAdoptionCandidate(
            [rich],
            owner,
            shopId,
            ttId,
            recordFourSellerShopId: 9002,
            claimed: [],
            incomingIds: [858570650911]);

        Assert.Same(rich, match);
    }

    [Fact]
    public void SelectAdoptionCandidate_SkipsRowsTheSamePageStillOwns()
    {
        var owner = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        const string ttId = "1732597616983053116";

        var alive = new Listing
        {
            OwnerId = owner,
            ShopId = shopId,
            FourSellerListingId = 855249580831,
            FourSellerShopId = 9002,
            TikTokProductId = ttId
        };

        Assert.Null(ListingSyncService.SelectAdoptionCandidate(
            [alive],
            owner,
            shopId,
            ttId,
            recordFourSellerShopId: 9003,
            claimed: [],
            // The page also carries the row's own id, so it is not a rename.
            incomingIds: [855249580831, 858570650911]));
    }

    [Fact]
    public void SelectAdoptionCandidate_SkipsTwinOnTheSameFourSellerShop()
    {
        var owner = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        const string ttId = "1732597616983053116";

        var twin = new Listing
        {
            OwnerId = owner,
            ShopId = shopId,
            FourSellerListingId = 855249580831,
            FourSellerShopId = 9002,
            TikTokProductId = ttId
        };

        Assert.Null(ListingSyncService.SelectAdoptionCandidate(
            [twin],
            owner,
            shopId,
            ttId,
            recordFourSellerShopId: 9002,
            claimed: [],
            incomingIds: [858570650911]));
    }

    [Fact]
    public void SelectAdoptionCandidate_PrefersShopMatchOverOrphan()
    {
        var owner = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        const string ttId = "1732597616983053116";

        var orphan = new Listing
        {
            OwnerId = owner,
            ShopId = null,
            FourSellerListingId = 855249580831,
            FourSellerShopId = 9001,
            TikTokProductId = ttId,
            RawPayload = "{\"ok\":true}",
            SourceUrl = "https://us.shein.com/orphan.html"
        };
        var onShop = new Listing
        {
            OwnerId = owner,
            ShopId = shopId,
            FourSellerListingId = 855249580832,
            FourSellerShopId = 9001,
            TikTokProductId = ttId
        };

        Assert.Same(onShop, ListingSyncService.SelectAdoptionCandidate(
            [orphan, onShop],
            owner,
            shopId,
            ttId,
            recordFourSellerShopId: 9002,
            claimed: [],
            incomingIds: [858570650911]));
    }

    [Fact]
    public void ListingSyncRequest_ApplyDefaults_PublishedStatuses_UsesStatusScope()
    {
        var request = new Application.DTOs.Listing.ListingSyncRequest
        {
            Statuses = [ListingSiteStatuses.Active, ListingSiteStatuses.Pending],
        };

        request.ApplyDefaults();

        Assert.Equal(ListingSyncScopes.Status, request.SyncScope);
    }
}