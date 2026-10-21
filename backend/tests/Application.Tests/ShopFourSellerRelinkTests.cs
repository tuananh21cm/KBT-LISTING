using Application.DTOs.FourSeller;
using Application.Services;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Shop sync must refresh <c>FourSellerShopId</c> via TikTok <c>ExternalShopId</c> when the
/// cookie owner differs from the local shop owner (reconnect after handover).
/// </summary>
public class ShopFourSellerRelinkTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ShopService _shops;

    public ShopFourSellerRelinkTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        var orderSync = new OrderSyncService(
            _db, null!, null!, null!, null!, null!, new NullFulfilQueueRealtimePublisher(),
            NullLogger<OrderSyncService>.Instance);
        _shops = new ShopService(
            _db,
            NewUserManager(_db),
            null!,
            orderSync,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<ShopService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Upsert_RelinksFourSellerIdAcrossOwners_KeepsOwnerAndPreviousId()
    {
        var localOwner = Guid.NewGuid();
        var cookieOwner = Guid.NewGuid();
        await SeedUserAsync(cookieOwner, "cookie-seller");
        var platform = await SeedPlatformAsync();
        var shop = new Shop
        {
            OwnerId = localOwner,
            Name = "Handed Over Shop",
            PlatformId = platform.Id,
            ExternalShopId = "7495000000000000001",
            FourSellerShopId = "100",
            Status = "Active"
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();

        var response = await _shops.UpsertFromFourSellerListAsync(
            cookieOwner,
            [
                new FourSellerShopRecordDto
                {
                    Id = 241316,
                    Platform = "TIKTOK",
                    PlatformShopName = "Handed Over Shop",
                    ShopName = "Handed Over Shop",
                    SellingPartnerId = "7495000000000000001",
                    StatusDesc = "Connected",
                    MarketplaceId = "US"
                }
            ],
            scopeToOwnerOnly: true);

        Assert.Equal(1, response.RelinkedCount);
        Assert.Equal(0, response.UpdatedCount);
        Assert.DoesNotContain(response.UnmatchedShops, s => s.Reason is "no_local_shop" or "ambiguous_external_id");

        await _db.Entry(shop).ReloadAsync();
        Assert.Equal(localOwner, shop.OwnerId);
        Assert.Equal("241316", shop.FourSellerShopId);
        Assert.Equal("100", shop.PreviousFourSellerShopId);
        Assert.Equal("Handed Over Shop", shop.Name);
    }

    [Fact]
    public async Task Upsert_AlreadyRelinkedShop_IsNotRewrittenOnEverySync()
    {
        var localOwner = Guid.NewGuid();
        var cookieOwner = Guid.NewGuid();
        await SeedUserAsync(cookieOwner, "cookie-seller");
        var platform = await SeedPlatformAsync();
        var shop = new Shop
        {
            OwnerId = localOwner,
            Name = "Handed Over Shop",
            PlatformId = platform.Id,
            ExternalShopId = "7495000000000000001",
            FourSellerShopId = "241316",
            PreviousFourSellerShopId = "100",
            Status = "Active"
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();
        var updatedAtBefore = shop.UpdatedAt;

        var response = await _shops.UpsertFromFourSellerListAsync(
            cookieOwner,
            [
                new FourSellerShopRecordDto
                {
                    Id = 241316,
                    Platform = "TIKTOK",
                    PlatformShopName = "Handed Over Shop",
                    ShopName = "Handed Over Shop",
                    SellingPartnerId = "7495000000000000001",
                    StatusDesc = "Connected",
                    MarketplaceId = "US"
                }
            ],
            scopeToOwnerOnly: true);

        Assert.Equal(0, response.RelinkedCount);
        Assert.Equal(0, response.SkippedCount);
        Assert.Empty(response.UnmatchedShops);

        await _db.Entry(shop).ReloadAsync();
        Assert.Equal("241316", shop.FourSellerShopId);
        Assert.Equal("100", shop.PreviousFourSellerShopId);
        Assert.Equal(updatedAtBefore, shop.UpdatedAt);
    }

    [Fact]
    public async Task Upsert_AmbiguousExternalId_DoesNotRelink()
    {
        var localOwnerA = Guid.NewGuid();
        var localOwnerB = Guid.NewGuid();
        var cookieOwner = Guid.NewGuid();
        await SeedUserAsync(cookieOwner, "cookie-seller");
        var platform = await SeedPlatformAsync();
        var ext = "7495000000000000099";
        _db.Shops.AddRange(
            new Shop
            {
                OwnerId = localOwnerA,
                Name = "Dup A",
                PlatformId = platform.Id,
                ExternalShopId = ext,
                FourSellerShopId = "10",
                Status = "Active"
            },
            new Shop
            {
                OwnerId = localOwnerB,
                Name = "Dup B",
                PlatformId = platform.Id,
                ExternalShopId = ext,
                FourSellerShopId = "11",
                Status = "Active"
            });
        await _db.SaveChangesAsync();

        var response = await _shops.UpsertFromFourSellerListAsync(
            cookieOwner,
            [
                new FourSellerShopRecordDto
                {
                    Id = 241314,
                    Platform = "TIKTOK",
                    PlatformShopName = "Dup",
                    ShopName = "Dup",
                    SellingPartnerId = ext,
                    StatusDesc = "Connected",
                    MarketplaceId = "US"
                }
            ],
            scopeToOwnerOnly: true);

        Assert.Equal(0, response.RelinkedCount);
        Assert.Contains(response.UnmatchedShops, s => s.Reason == "ambiguous_external_id");
        Assert.All(
            await _db.Shops.ToListAsync(),
            s => Assert.True(s.FourSellerShopId is "10" or "11"));
    }

    [Fact]
    public async Task Upsert_BlankSellingPartnerId_KeepsStoredExternalShopId()
    {
        var owner = Guid.NewGuid();
        await SeedUserAsync(owner, "cookie-seller");
        var platform = await SeedPlatformAsync();
        var shop = new Shop
        {
            OwnerId = owner,
            Name = "Kept Shop",
            PlatformId = platform.Id,
            ExternalShopId = "7495000000000000001",
            FourSellerShopId = "100",
            Status = "Active"
        };
        _db.Shops.Add(shop);
        await _db.SaveChangesAsync();

        var response = await _shops.UpsertFromFourSellerListAsync(
            owner,
            [
                new FourSellerShopRecordDto
                {
                    Id = 241316,
                    Platform = "TIKTOK",
                    PlatformShopName = "Kept Shop",
                    ShopName = "Kept Shop",
                    SellingPartnerId = string.Empty,
                    StatusDesc = "Connected",
                    MarketplaceId = "US"
                }
            ],
            scopeToOwnerOnly: true);

        Assert.Equal(1, response.UpdatedCount);

        await _db.Entry(shop).ReloadAsync();
        Assert.Equal("7495000000000000001", shop.ExternalShopId);
        Assert.Equal("241316", shop.FourSellerShopId);
        Assert.Equal("100", shop.PreviousFourSellerShopId);
    }

    [Fact]
    public async Task Upsert_NoLocalShop_ReportsUnmatched()
    {
        var cookieOwner = Guid.NewGuid();
        await SeedUserAsync(cookieOwner, "cookie-seller");
        await SeedPlatformAsync();

        var response = await _shops.UpsertFromFourSellerListAsync(
            cookieOwner,
            [
                new FourSellerShopRecordDto
                {
                    Id = 241315,
                    Platform = "TIKTOK",
                    PlatformShopName = "Brand New",
                    ShopName = "Brand New",
                    SellingPartnerId = "7495111111111111111",
                    StatusDesc = "Connected",
                    MarketplaceId = "US"
                }
            ],
            scopeToOwnerOnly: true);

        Assert.Equal(0, response.RelinkedCount);
        Assert.Contains(response.UnmatchedShops, s =>
            s.Reason == "no_local_shop" && s.FourSellerShopId == 241315);
    }

    private async Task SeedUserAsync(Guid id, string userName)
    {
        _db.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.local",
            NormalizedEmail = $"{userName}@TEST.LOCAL".ToUpperInvariant(),
            FullName = userName,
            IsActive = true
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Platform> SeedPlatformAsync()
    {
        var platform = new Platform
        {
            Id = Guid.NewGuid(),
            Code = "TIKTOK",
            Name = "TikTok",
            IsActive = true
        };
        _db.Platforms.Add(platform);
        await _db.SaveChangesAsync();
        return platform;
    }

    private static UserManager<ApplicationUser> NewUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, IdentityRole<Guid>, ApplicationDbContext, Guid>(db);
        return new UserManager<ApplicationUser>(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }
}
