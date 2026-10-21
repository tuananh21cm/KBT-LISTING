using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

public class ShopOwnershipTransferServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ShopOwnershipTransferService _service;

    public ShopOwnershipTransferServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new ShopOwnershipTransferService(
            _db,
            NullLogger<ShopOwnershipTransferService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task TransferAsync_ReassignsOrdersAndListingsOnShop()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "1001");
        var otherShop = await SeedShopAsync(from, "2002");

        _db.Orders.AddRange(
            new Order
            {
                OwnerId = from,
                ShopId = shop.Id,
                FourSellerOrderId = 1,
                FourSellerShopId = 1001,
                PlatformOrderId = "PO-1",
                OrderStatus = "to_ship"
            },
            new Order
            {
                OwnerId = from,
                ShopId = otherShop.Id,
                FourSellerOrderId = 2,
                FourSellerShopId = 2002,
                PlatformOrderId = "PO-2",
                OrderStatus = "to_ship"
            });
        _db.Listings.Add(new Listing
        {
            OwnerId = from,
            ShopId = shop.Id,
            FourSellerListingId = 11,
            FourSellerShopId = 1001,
            ProductName = "L1",
            PageType = "published"
        });
        await _db.SaveChangesAsync();

        shop.OwnerId = to;
        await _db.SaveChangesAsync();

        var result = await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.OrdersTransferred);
        Assert.Equal(1, result.ListingsTransferred);
        Assert.Equal(to, await _db.Orders.Where(o => o.FourSellerOrderId == 1).Select(o => o.OwnerId).SingleAsync());
        Assert.Equal(from, await _db.Orders.Where(o => o.FourSellerOrderId == 2).Select(o => o.OwnerId).SingleAsync());
        Assert.Equal(to, await _db.Listings.Select(l => l.OwnerId).SingleAsync());
    }

    [Fact]
    public async Task TransferAsync_RematchesByFourSellerShopIdThenTransfers()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "5555");

        _db.Orders.Add(new Order
        {
            OwnerId = from,
            ShopId = null,
            FourSellerOrderId = 99,
            FourSellerShopId = 5555,
            PlatformOrderId = "576499284339103507",
            OrderStatus = "to_ship"
        });
        await _db.SaveChangesAsync();

        var result = await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.OrdersRematched);
        Assert.Equal(1, result.OrdersTransferred);
        var order = await _db.Orders.SingleAsync();
        Assert.Equal(shop.Id, order.ShopId);
        Assert.Equal(to, order.OwnerId);
    }

    [Fact]
    public async Task TransferAsync_MergesPlatformOrderIdConflict()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "1001");

        _db.Orders.AddRange(
            new Order
            {
                OwnerId = from,
                ShopId = shop.Id,
                FourSellerOrderId = 1,
                FourSellerShopId = 1001,
                PlatformOrderId = "DUP",
                OrderStatus = "to_ship"
            },
            new Order
            {
                OwnerId = to,
                ShopId = shop.Id,
                FourSellerOrderId = 2,
                FourSellerShopId = 1001,
                PlatformOrderId = "DUP",
                OrderStatus = "to_ship",
                TrackingNo = "GFUS01072265967621"
            });
        await _db.SaveChangesAsync();

        var result = await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.OrdersMerged);
        var order = await _db.Orders.SingleAsync(o => o.PlatformOrderId == "DUP");
        Assert.Equal(to, order.OwnerId);
        Assert.Equal(shop.Id, order.ShopId);
        Assert.Equal("GFUS01072265967621", order.TrackingNo);
    }

    [Fact]
    public async Task ReassignMismatchedOwnedDataAsync_MergesDuplicateSellersOnShop()
    {
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(newOwner, "117");

        _db.Orders.AddRange(
            new Order
            {
                OwnerId = oldOwner,
                ShopId = shop.Id,
                FourSellerOrderId = 10,
                FourSellerShopId = 117,
                PlatformOrderId = "576499284339103507",
                OrderStatus = "to_ship"
            },
            new Order
            {
                OwnerId = newOwner,
                ShopId = shop.Id,
                FourSellerOrderId = 11,
                FourSellerShopId = 117,
                PlatformOrderId = "576499284339103507",
                OrderStatus = "to_ship",
                TrackingNo = "TRACK-KEEP"
            });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignMismatchedOwnedDataAsync(shop.Id);

        Assert.True(result.OrdersMerged >= 1);
        var order = Assert.Single(await _db.Orders.ToListAsync());
        Assert.Equal(newOwner, order.OwnerId);
        Assert.Equal("TRACK-KEEP", order.TrackingNo);
    }

    [Fact]
    public void OrderOwnershipMerge_ChooseKeep_PrefersPreferredOwner()
    {
        var preferred = Guid.NewGuid();
        var other = Guid.NewGuid();
        var a = new Order { OwnerId = other, PlatformOrderId = "1" };
        var b = new Order { OwnerId = preferred, PlatformOrderId = "1", TrackingNo = "T" };

        Assert.Same(b, OrderOwnershipMerge.ChooseKeep(a, b, preferred));
    }

    [Fact]
    public async Task ReassignMismatchedOwnedDataAsync_BackfillsStaleOwner()
    {
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(newOwner, "7777");

        _db.Orders.Add(new Order
        {
            OwnerId = oldOwner,
            ShopId = shop.Id,
            FourSellerOrderId = 7,
            FourSellerShopId = 7777,
            PlatformOrderId = "PO-BACKFILL",
            OrderStatus = "shipped"
        });
        _db.ListingAnalyticsSnapshots.Add(new ListingAnalyticsSnapshot
        {
            ShopId = shop.Id,
            OwnerId = oldOwner,
            TikTokProductId = "p1",
            SnapshotDate = DateOnly.FromDateTime(DateTime.UtcNow),
            RangeStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            RangeEnd = DateOnly.FromDateTime(DateTime.UtcNow),
            Preset = "last7days"
        });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignMismatchedOwnedDataAsync(shop.Id);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.OrdersTransferred);
        Assert.Equal(newOwner, await _db.Orders.Select(o => o.OwnerId).SingleAsync());
        Assert.Equal(newOwner, await _db.ListingAnalyticsSnapshots.Select(s => s.OwnerId).SingleAsync());
    }

    [Fact]
    public async Task TransferAsync_BackfillsEmptyListingSourceUrlFromOrderItemSku()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "4242");

        var listingId = Guid.NewGuid();
        _db.Listings.Add(new Listing
        {
            Id = listingId,
            OwnerId = from,
            ShopId = shop.Id,
            FourSellerListingId = 424201,
            FourSellerShopId = 4242,
            ProductName = "Need URL",
            PageType = "published",
            SourceUrl = null
        });
        _db.ListingVariations.Add(new ListingVariation
        {
            ListingId = listingId,
            SellerSku = "SKU-BACKFILL-1",
            Quantity = 1
        });

        var orderId = Guid.NewGuid();
        _db.Orders.Add(new Order
        {
            Id = orderId,
            OwnerId = from,
            ShopId = shop.Id,
            FourSellerOrderId = 4242,
            FourSellerShopId = 4242,
            PlatformOrderId = "PO-SRC",
            OrderStatus = "to_ship"
        });
        _db.OrderItems.Add(new OrderItem
        {
            OrderId = orderId,
            SellerSku = "SKU-BACKFILL-1",
            SourceUrl = "https://us.shein.com/item-p-111.html",
            Quantity = 1,
            Title = "Need URL"
        });
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _db.ListingVariations.CountAsync());
        Assert.Equal(1, await _db.OrderItems.CountAsync(i => i.SourceUrl != null));

        shop.OwnerId = to;
        await _db.SaveChangesAsync();

        await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        var updated = await _db.Listings.SingleAsync();
        Assert.Equal(to, updated.OwnerId);
        Assert.Equal("https://us.shein.com/item-p-111.html", updated.SourceUrl);
    }

    [Fact]
    public async Task ReassignMismatchedOwnedDataAsync_CollapsesThinShellOntoRichListing()
    {
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(newOwner, "9191");
        var ttId = "1732624461708431811";

        _db.Listings.AddRange(
            new Listing
            {
                OwnerId = oldOwner,
                ShopId = shop.Id,
                FourSellerListingId = 857562180908,
                FourSellerShopId = 9191,
                TikTokProductId = ttId,
                ProductName = "Rich floral",
                PageType = "published",
                SourceUrl = "https://us.shein.com/rich-floral.html",
                RawPayload = "{\"ok\":true}",
                VariationCount = 8
            },
            new Listing
            {
                OwnerId = newOwner,
                ShopId = shop.Id,
                FourSellerListingId = 858594510911,
                FourSellerShopId = 9191,
                TikTokProductId = ttId,
                ProductName = "Thin floral",
                PageType = "published",
                SourceUrl = null,
                RawPayload = null,
                VariationCount = 0
            });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignMismatchedOwnedDataAsync(shop.Id);

        Assert.True(result.ListingsCollapsed >= 1);
        var kept = Assert.Single(await _db.Listings.ToListAsync());
        Assert.Equal(newOwner, kept.OwnerId);
        Assert.Equal(857562180908, kept.FourSellerListingId);
        Assert.Equal("https://us.shein.com/rich-floral.html", kept.SourceUrl);
        Assert.False(string.IsNullOrWhiteSpace(kept.RawPayload));
    }

    [Fact]
    public async Task ReassignAllMismatchedOwnedDataAsync_ProcessesMismatchShop()
    {
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(newOwner, "8282");

        _db.Listings.Add(new Listing
        {
            OwnerId = oldOwner,
            ShopId = shop.Id,
            FourSellerListingId = 828201,
            FourSellerShopId = 8282,
            ProductName = "Stale owner",
            PageType = "published",
            SourceUrl = "https://us.shein.com/stale.html",
            RawPayload = "{}"
        });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignAllMismatchedOwnedDataAsync();

        Assert.True(result.ShopsProcessed >= 1);
        Assert.Equal(1, result.ListingsTransferred);
        Assert.Equal(newOwner, await _db.Listings.Select(l => l.OwnerId).SingleAsync());
    }

    [Fact]
    public async Task ReassignMismatchedOwnedDataAsync_RematchesSameOwnerOrphanShellAndCollapses()
    {
        var owner = Guid.NewGuid();
        var shop = await SeedShopAsync(owner, "9192");
        const string ttId = "1732604963830010137";

        _db.Listings.AddRange(
            new Listing
            {
                OwnerId = owner,
                ShopId = shop.Id,
                FourSellerListingId = 855249580831,
                FourSellerShopId = 9192,
                TikTokProductId = ttId,
                ProductName = "Rich pajama",
                PageType = "published",
                SourceUrl = "https://us.shein.com/rich-pajama.html",
                RawPayload = "{\"ok\":true}",
                VariationCount = 12
            },
            new Listing
            {
                OwnerId = owner,
                ShopId = null,
                FourSellerListingId = 858543900911,
                FourSellerShopId = 9192,
                TikTokProductId = ttId,
                ProductName = "Orphan shell",
                PageType = "published",
                SourceUrl = null,
                RawPayload = null,
                VariationCount = 12
            });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignMismatchedOwnedDataAsync(shop.Id);

        Assert.True(result.ListingsRematched >= 1);
        Assert.True(result.ListingsCollapsed >= 1);
        var kept = Assert.Single(await _db.Listings.ToListAsync());
        Assert.Equal(shop.Id, kept.ShopId);
        Assert.Equal(855249580831, kept.FourSellerListingId);
        Assert.Equal("https://us.shein.com/rich-pajama.html", kept.SourceUrl);
    }

    [Fact]
    public async Task ReassignAllMismatchedOwnedDataAsync_IncludesShopWithOrphanShell()
    {
        var owner = Guid.NewGuid();
        var shop = await SeedShopAsync(owner, "9193");
        const string ttId = "1732604288166891801";

        _db.Listings.AddRange(
            new Listing
            {
                OwnerId = owner,
                ShopId = shop.Id,
                FourSellerListingId = 855237830831,
                FourSellerShopId = 9193,
                TikTokProductId = ttId,
                ProductName = "Rich nightgown",
                PageType = "published",
                SourceUrl = "https://us.shein.com/rich-nightgown.html",
                RawPayload = "{\"ok\":true}",
                VariationCount = 24
            },
            new Listing
            {
                OwnerId = owner,
                ShopId = null,
                FourSellerListingId = 858543930911,
                FourSellerShopId = 9193,
                TikTokProductId = ttId,
                ProductName = "Orphan shell",
                PageType = "published",
                SourceUrl = null,
                RawPayload = null,
                VariationCount = 24
            });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignAllMismatchedOwnedDataAsync();

        Assert.True(result.ShopsProcessed >= 1);
        Assert.True(result.ListingsCollapsed >= 1);
        Assert.False(await _db.Listings.AnyAsync(l => l.FourSellerListingId == 858543930911));
        var kept = Assert.Single(await _db.Listings.Where(l => l.TikTokProductId == ttId).ToListAsync());
        Assert.Equal("https://us.shein.com/rich-nightgown.html", kept.SourceUrl);
    }

    [Fact]
    public async Task CollapseShopDuplicatesAsync_MergesShellFactsOntoRichRow()
    {
        var owner = Guid.NewGuid();
        var shop = await SeedShopAsync(owner, "9194");
        const string ttId = "1732604288166891802";
        var supplierInfoId = Guid.NewGuid();

        _db.Listings.AddRange(
            new Listing
            {
                OwnerId = owner,
                ShopId = shop.Id,
                FourSellerListingId = 855237830832,
                FourSellerShopId = 9194,
                TikTokProductId = ttId,
                ProductName = "Rich dress",
                PageType = "published",
                RawPayload = "{\"ok\":true}",
                VariationCount = 6
            },
            new Listing
            {
                OwnerId = owner,
                ShopId = shop.Id,
                FourSellerListingId = 858543930912,
                FourSellerShopId = 9194,
                TikTokProductId = ttId,
                ProductName = "Shell dress",
                PageType = "published",
                SourceUrl = "https://us.shein.com/rich-dress.html",
                SupplierProductInfoId = supplierInfoId,
                ReviewCount = 128,
                AverageRating = 4.7m,
                VariationCount = 6
            });
        await _db.SaveChangesAsync();

        var collapsed = await _service.CollapseShopDuplicatesAsync(new[] { shop.Id });

        Assert.Equal(1, collapsed);
        var kept = Assert.Single(await _db.Listings.ToListAsync());
        Assert.Equal(855237830832, kept.FourSellerListingId);
        Assert.Equal("https://us.shein.com/rich-dress.html", kept.SourceUrl);
        Assert.Equal(supplierInfoId, kept.SupplierProductInfoId);
        Assert.Equal(128, kept.ReviewCount);
        Assert.Equal(4.7m, kept.AverageRating);
    }

    [Fact]
    public async Task CollapseShopDuplicatesAsync_CollapsesAcrossSiblingShopRows()
    {
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var oldShop = await SeedShopAsync(oldOwner, "9195");
        var newShop = await SeedShopAsync(newOwner, "9196");
        oldShop.ExternalShopId = "7495000000000000001";
        newShop.ExternalShopId = "7495000000000000001";
        await _db.SaveChangesAsync();

        const string ttId = "1732604288166891803";
        _db.Listings.AddRange(
            new Listing
            {
                OwnerId = oldOwner,
                ShopId = oldShop.Id,
                FourSellerListingId = 855237830833,
                FourSellerShopId = 9195,
                TikTokProductId = ttId,
                ProductName = "Rich skirt",
                PageType = "published",
                SourceUrl = "https://us.shein.com/rich-skirt.html",
                RawPayload = "{\"ok\":true}"
            },
            new Listing
            {
                OwnerId = newOwner,
                ShopId = newShop.Id,
                FourSellerListingId = 858543930913,
                FourSellerShopId = 9196,
                TikTokProductId = ttId,
                ProductName = "Shell skirt",
                PageType = "published"
            });
        await _db.SaveChangesAsync();

        var collapsed = await _service.CollapseShopDuplicatesAsync(new[] { newShop.Id });

        Assert.Equal(1, collapsed);
        var kept = Assert.Single(await _db.Listings.ToListAsync());
        Assert.Equal("https://us.shein.com/rich-skirt.html", kept.SourceUrl);
        Assert.Equal(newShop.Id, kept.ShopId);
        Assert.Equal(newOwner, kept.OwnerId);
    }

    [Fact]
    public async Task TransferAsync_RematchesListingsCarryingThePreviousFourSellerShopId()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "7002");
        shop.PreviousFourSellerShopId = "7001";
        await _db.SaveChangesAsync();

        _db.Listings.Add(new Listing
        {
            OwnerId = from,
            ShopId = null,
            FourSellerListingId = 855237830834,
            FourSellerShopId = 7001,
            ProductName = "Listing from the old 4Seller account",
            PageType = "published",
            SourceUrl = "https://us.shein.com/old-account.html"
        });
        await _db.SaveChangesAsync();

        var result = await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.ListingsRematched);
        Assert.Equal(1, result.ListingsTransferred);
        var listing = await _db.Listings.SingleAsync();
        Assert.Equal(shop.Id, listing.ShopId);
        Assert.Equal(to, listing.OwnerId);
    }

    [Fact]
    public async Task TransferAsync_ClearsReceivingSellerSyncWatermarks()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "7003");

        _db.ListingSyncWatermarks.Add(new ListingSyncWatermark
        {
            OwnerId = to,
            Platform = "tiktok",
            Status = "active"
        });
        await _db.SaveChangesAsync();

        await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        Assert.False(await _db.ListingSyncWatermarks.AnyAsync(w => w.OwnerId == to));
    }

    [Fact]
    public async Task ReassignAllMismatchedOwnedDataAsync_PicksUpOrphanOrdersByFourSellerShopId()
    {
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();
        var shop = await SeedShopAsync(newOwner, "241316");

        _db.Orders.Add(new Order
        {
            OwnerId = oldOwner,
            ShopId = null,
            FourSellerOrderId = 77,
            FourSellerShopId = 241316,
            PlatformOrderId = "577578518813118642",
            OrderStatus = "shipped"
        });
        await _db.SaveChangesAsync();

        var result = await _service.ReassignAllMismatchedOwnedDataAsync();

        Assert.True(result.OrdersTransferred >= 1 || result.OrdersRematched >= 1);
        var order = await _db.Orders.SingleAsync();
        Assert.Equal(shop.Id, order.ShopId);
        Assert.Equal(newOwner, order.OwnerId);
    }

    [Fact]
    public void OrderFourSellerOwner_PrefersShopOwner()
    {
        var shopOwner = Guid.NewGuid();
        var orderOwner = Guid.NewGuid();
        var order = new Order
        {
            OwnerId = orderOwner,
            Shop = new Shop { OwnerId = shopOwner, Name = "S" }
        };

        Assert.Equal(shopOwner, OrderFourSellerOwner.Resolve(order));
    }

    [Fact]
    public void OrderFourSellerOwner_FallsBackToOrderOwner()
    {
        var orderOwner = Guid.NewGuid();
        var order = new Order { OwnerId = orderOwner };

        Assert.Equal(orderOwner, OrderFourSellerOwner.Resolve(order));
    }

    [Fact]
    public async Task TransferAsync_MergesPlatformOrderIdConflict_WhenBothOrdersCarryIssueTags()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "1001");
        var sharedTag = await SeedIssueTagAsync("Hết size");
        var dropOnlyTag = await SeedIssueTagAsync("Sai màu");

        var drop = new Order
        {
            OwnerId = from,
            ShopId = shop.Id,
            FourSellerOrderId = 1,
            FourSellerShopId = 1001,
            PlatformOrderId = "DUP",
            OrderStatus = "to_ship",
            IssueStatus = OrderIssueStatuses.Processing
        };
        var keep = new Order
        {
            OwnerId = to,
            ShopId = shop.Id,
            FourSellerOrderId = 2,
            FourSellerShopId = 1001,
            PlatformOrderId = "DUP",
            OrderStatus = "to_ship",
            TrackingNo = "GFUS01072265967621"
        };
        _db.Orders.AddRange(drop, keep);
        _db.OrderIssueTags.AddRange(
            new OrderIssueTag { OrderId = drop.Id, IssueTagId = sharedTag },
            new OrderIssueTag { OrderId = drop.Id, IssueTagId = dropOnlyTag },
            new OrderIssueTag { OrderId = keep.Id, IssueTagId = sharedTag });
        await _db.SaveChangesAsync();

        var result = await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        Assert.Equal(1, result.OrdersMerged);
        var surviving = Assert.Single(await _db.Orders.ToListAsync());
        Assert.Equal(keep.Id, surviving.Id);
        Assert.Equal(to, surviving.OwnerId);

        // The denorm has to follow the tags, else the order drops out of the issue queue on /orders.
        Assert.Equal(OrderIssueStatuses.Processing, surviving.IssueStatus);

        var tagIds = await _db.OrderIssueTags
            .Where(t => t.OrderId == surviving.Id)
            .Select(t => t.IssueTagId)
            .ToListAsync();
        Assert.Equal(2, tagIds.Count);
        Assert.Contains(sharedTag, tagIds);
        Assert.Contains(dropOnlyTag, tagIds);
    }

    [Fact]
    public async Task ReassignMismatchedOwnedDataAsync_MergesThreeOrdersSharingOneIssueTagOntoOneKeep()
    {
        var newOwner = Guid.NewGuid();
        var staleA = Guid.NewGuid();
        var staleB = Guid.NewGuid();
        var shop = await SeedShopAsync(newOwner, "3003");
        var tag = await SeedIssueTagAsync("Hết hàng");

        var dropA = new Order
        {
            OwnerId = staleA,
            ShopId = shop.Id,
            FourSellerOrderId = 31,
            FourSellerShopId = 3003,
            PlatformOrderId = "TRIPLE",
            OrderStatus = "to_ship"
        };
        var dropB = new Order
        {
            OwnerId = staleB,
            ShopId = shop.Id,
            FourSellerOrderId = 32,
            FourSellerShopId = 3003,
            PlatformOrderId = "TRIPLE",
            OrderStatus = "to_ship"
        };
        var keep = new Order
        {
            OwnerId = newOwner,
            ShopId = shop.Id,
            FourSellerOrderId = 33,
            FourSellerShopId = 3003,
            PlatformOrderId = "TRIPLE",
            OrderStatus = "to_ship",
            TrackingNo = "TRACK-KEEP"
        };
        _db.Orders.AddRange(dropA, dropB, keep);

        // Both losers carry the same tag: the second merge only sees the first insert in the change
        // tracker, so a store-only lookup would re-add a key already tracked.
        _db.OrderIssueTags.AddRange(
            new OrderIssueTag { OrderId = dropA.Id, IssueTagId = tag },
            new OrderIssueTag { OrderId = dropB.Id, IssueTagId = tag });
        await _db.SaveChangesAsync();

        await _service.ReassignMismatchedOwnedDataAsync(shop.Id);
        await _db.SaveChangesAsync();

        var surviving = Assert.Single(await _db.Orders.ToListAsync());
        Assert.Equal(keep.Id, surviving.Id);
        Assert.Equal(newOwner, surviving.OwnerId);

        var row = Assert.Single(await _db.OrderIssueTags.ToListAsync());
        Assert.Equal(surviving.Id, row.OrderId);
        Assert.Equal(tag, row.IssueTagId);
    }

    [Fact]
    public async Task TransferAsync_KeepsFulfilAttemptsOfMergedOrder()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var fulfilUser = Guid.NewGuid();
        var shop = await SeedShopAsync(from, "4004");

        var drop = new Order
        {
            OwnerId = from,
            ShopId = shop.Id,
            FourSellerOrderId = 41,
            FourSellerShopId = 4004,
            PlatformOrderId = "ATTEMPT",
            OrderStatus = "to_ship"
        };
        var keep = new Order
        {
            OwnerId = to,
            ShopId = shop.Id,
            FourSellerOrderId = 42,
            FourSellerShopId = 4004,
            PlatformOrderId = "ATTEMPT",
            OrderStatus = "to_ship",
            TrackingNo = "TRACK-KEEP"
        };
        _db.Orders.AddRange(drop, keep);

        var startedAt = DateTime.UtcNow.AddMinutes(-20);
        _db.OrderFulfilAttempts.Add(new OrderFulfilAttempt
        {
            OrderId = drop.Id,
            UserId = fulfilUser,
            SessionId = Guid.NewGuid(),
            BatchId = Guid.NewGuid(),
            StartedAt = startedAt,
            LastRenewedAt = startedAt.AddMinutes(3),
            ExpiresAt = startedAt.AddMinutes(10),
            ClosedAt = startedAt.AddMinutes(4),
            CompletedAt = startedAt.AddMinutes(4),
            Outcome = FulfilAttemptOutcomes.Purchased,
            DurationSeconds = 240
        });
        await _db.SaveChangesAsync();

        await _service.TransferAsync(shop.Id, from, to);
        await _db.SaveChangesAsync();

        var attempt = Assert.Single(await _db.OrderFulfilAttempts.ToListAsync());
        Assert.Equal(keep.Id, attempt.OrderId);
        Assert.Equal(FulfilAttemptOutcomes.Purchased, attempt.Outcome);
    }

    private async Task<Guid> SeedIssueTagAsync(string label)
    {
        var tag = new IssueTag { Label = label, IsSystem = true };
        _db.IssueTags.Add(tag);
        await _db.SaveChangesAsync();
        return tag.Id;
    }

    private async Task<Shop> SeedShopAsync(Guid ownerId, string fourSellerShopId)
    {
        var platformId = Guid.NewGuid();
        _db.Platforms.Add(new Platform
        {
            Id = platformId,
            Code = "TIKTOK",
            Name = "TikTok",
            IsActive = true
        });
        var shop = new Shop
        {
            OwnerId = ownerId,
            Name = $"Shop-{fourSellerShopId}",
            PlatformId = platformId,
            FourSellerShopId = fourSellerShopId,
            Status = "Active"
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();
        return shop;
    }
}
