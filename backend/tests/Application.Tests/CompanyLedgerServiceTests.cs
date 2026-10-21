using Application.DTOs.CompanyLedger;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Company ledger: one acquisition row per shop, independent seller/shop assignment, seller scope.
/// </summary>
public class CompanyLedgerServiceTests
{
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public async Task UpsertAcquisition_CreatesThenUpdatesSameRow_AndKeepsSellerOnLaterEdit()
    {
        await using var db = NewDb();
        var owner = await SeedSellerAsync(db, "owner-a");
        var shop = NewShop(owner.Id, "Shop A");
        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var created = await service.UpsertShopAcquisitionAsync(
            shop.Id,
            new UpsertShopAcquisitionCostDto { Amount = 120m },
            ActorId);
        Assert.NotNull(created);
        Assert.Equal(CompanyLedgerEntry.SourceShopAcquisition, created!.Source);
        Assert.Equal(owner.Id, created.OwnerUserId);
        Assert.Equal(120m, created.Amount);

        shop.OwnerId = Guid.NewGuid();
        await db.SaveChangesAsync();

        var updated = await service.UpsertShopAcquisitionAsync(
            shop.Id,
            new UpsertShopAcquisitionCostDto { Amount = 80m },
            ActorId);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal(80m, updated.Amount);
        Assert.Equal(owner.Id, updated.OwnerUserId);
        Assert.Equal(1, await db.CompanyLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task UpsertAcquisition_ZeroDeletesRow()
    {
        await using var db = NewDb();
        var owner = await SeedSellerAsync(db, "owner-b");
        var shop = NewShop(owner.Id, "Shop B");
        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.UpsertShopAcquisitionAsync(shop.Id, new UpsertShopAcquisitionCostDto { Amount = 10m }, ActorId);
        var deleted = await service.UpsertShopAcquisitionAsync(
            shop.Id, new UpsertShopAcquisitionCostDto { Amount = 0m }, ActorId);

        Assert.Null(deleted);
        Assert.Empty(await db.CompanyLedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task Create_ShopAcquisitionWithShop_ReusesUniqueRow()
    {
        await using var db = NewDb();
        var owner = await SeedSellerAsync(db, "owner-c");
        var other = await SeedSellerAsync(db, "other-c");
        var shop = NewShop(owner.Id, "Shop C");
        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var first = await service.CreateAsync(Acquisition(shop.Id, owner.Id, 50m), ActorId);
        var vnd = Acquisition(shop.Id, other.Id, 70m);
        vnd.Currency = "VND";
        var second = await service.CreateAsync(vnd, ActorId);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(70m, second.Amount);
        Assert.Equal("USD", second.Currency);
        Assert.Equal(other.Id, second.OwnerUserId);
        Assert.Equal(CompanyLedgerEntry.DirectionDebit, second.Direction);
        Assert.Equal(1, await db.CompanyLedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Create_AllowsEveryAssignmentCombination_IncludingMismatchedShopOwner()
    {
        await using var db = NewDb();
        var seller = await SeedSellerAsync(db, "seller-d");
        var shopOwner = await SeedSellerAsync(db, "shop-owner-d");
        var shop = NewShop(shopOwner.Id, "Shop D");
        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.CreateAsync(Manual(null, null), ActorId);
        await service.CreateAsync(Manual(seller.Id, null), ActorId);
        await service.CreateAsync(Manual(null, shop.Id), ActorId);
        await service.CreateAsync(Manual(seller.Id, shop.Id), ActorId);

        var rows = await db.CompanyLedgerEntries.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(CompanyLedgerEntry.SourceManual, r.Source));
        Assert.Contains(rows, r => r.OwnerUserId == seller.Id && r.ShopId == shop.Id);
    }

    [Fact]
    public async Task Update_ClearingShop_ReleasesAcquisitionSource()
    {
        await using var db = NewDb();
        var owner = await SeedSellerAsync(db, "owner-e");
        var shop = NewShop(owner.Id, "Shop E");
        db.Shops.Add(shop);
        await db.SaveChangesAsync();

        var service = NewService(db);
        var created = await service.UpsertShopAcquisitionAsync(
            shop.Id, new UpsertShopAcquisitionCostDto { Amount = 15m }, ActorId);

        var updated = await service.UpdateAsync(created!.Id, new SaveCompanyLedgerEntryDto
        {
            Direction = CompanyLedgerEntry.DirectionDebit,
            CategoryCode = CompanyLedgerEntry.CategoryProxy,
            Amount = 15m,
            Currency = "USD",
            Title = "Proxy sau khi gỡ shop",
            OccurredAt = DateTime.UtcNow,
            ShopId = null,
            OwnerUserId = owner.Id
        });

        Assert.Equal(CompanyLedgerEntry.SourceManual, updated.Source);
        Assert.Equal(CompanyLedgerEntry.CategoryProxy, updated.CategoryCode);
        Assert.Null(updated.ShopId);
    }

    [Fact]
    public async Task Update_MovingOntoShopThatAlreadyHasAcquisition_Throws()
    {
        await using var db = NewDb();
        var owner = await SeedSellerAsync(db, "owner-f");
        var shopA = NewShop(owner.Id, "Shop FA");
        var shopB = NewShop(owner.Id, "Shop FB");
        db.Shops.AddRange(shopA, shopB);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.UpsertShopAcquisitionAsync(shopA.Id, new UpsertShopAcquisitionCostDto { Amount = 1m }, ActorId);
        var rowB = await service.UpsertShopAcquisitionAsync(
            shopB.Id, new UpsertShopAcquisitionCostDto { Amount = 2m }, ActorId);

        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateAsync(rowB!.Id, Acquisition(shopA.Id, owner.Id, 9m)));
    }

    [Fact]
    public async Task List_SellerSeesAssignedSellerOrOwnedShop_NotCompanyOnlyRows()
    {
        await using var db = NewDb();
        var seller = await SeedSellerAsync(db, "seller-g");
        var other = await SeedSellerAsync(db, "other-g");
        var shop = NewShop(seller.Id, "Shop G");
        var otherShop = NewShop(other.Id, "Shop G2");
        db.Shops.AddRange(shop, otherShop);
        await db.SaveChangesAsync();

        var service = NewService(db);
        await service.CreateAsync(Manual(null, null, "company"), ActorId);
        await service.CreateAsync(Manual(seller.Id, null, "mine"), ActorId);
        await service.CreateAsync(Manual(null, shop.Id, "my-shop"), ActorId);
        await service.CreateAsync(Manual(other.Id, otherShop.Id, "theirs"), ActorId);

        var visible = await service.ListAsync(new CompanyLedgerQuery { SellerScopeUserId = seller.Id });
        var titles = visible.Items.Select(i => i.Title).ToHashSet();
        Assert.Equal(2, titles.Count);
        Assert.Contains("mine", titles);
        Assert.Contains("my-shop", titles);
        Assert.DoesNotContain("company", titles);
        Assert.DoesNotContain("theirs", titles);
    }

    private static SaveCompanyLedgerEntryDto Acquisition(Guid shopId, Guid ownerId, decimal amount) => new()
    {
        Direction = CompanyLedgerEntry.DirectionCredit,
        CategoryCode = CompanyLedgerEntry.CategoryShopAcquisition,
        Amount = amount,
        Currency = "USD",
        Title = "Giá mua",
        OccurredAt = DateTime.UtcNow,
        ShopId = shopId,
        OwnerUserId = ownerId
    };

    private static SaveCompanyLedgerEntryDto Manual(Guid? ownerId, Guid? shopId, string title = "Proxy") => new()
    {
        Direction = CompanyLedgerEntry.DirectionDebit,
        CategoryCode = CompanyLedgerEntry.CategoryProxy,
        Amount = 5m,
        Currency = "USD",
        Title = title,
        OccurredAt = DateTime.UtcNow,
        ShopId = shopId,
        OwnerUserId = ownerId
    };

    private static CompanyLedgerService NewService(ApplicationDbContext db) =>
        new(db, NewUserManager(db));

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<ApplicationUser> SeedSellerAsync(ApplicationDbContext db, string name)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Seller);
        if (role is null)
        {
            role = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = RoleNames.Seller,
                NormalizedName = RoleNames.Seller.ToUpperInvariant()
            };
            db.Roles.Add(role);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            FullName = name,
            IsActive = true,
            Email = $"{name}@test.local",
            NormalizedEmail = $"{name}@test.local".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        db.Set<IdentityUserRole<Guid>>().Add(new IdentityUserRole<Guid>
        {
            UserId = user.Id,
            RoleId = role.Id
        });
        await db.SaveChangesAsync();
        return user;
    }

    private static Shop NewShop(Guid ownerId, string name) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ownerId,
        Name = name,
        PlatformId = Guid.NewGuid(),
        Status = "Active",
        Region = "US",
        Currency = "USD"
    };

    private static UserManager<ApplicationUser> NewUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db);
        return new UserManager<ApplicationUser>(
            store,
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }
}
