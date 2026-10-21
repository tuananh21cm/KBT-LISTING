using Application.Services;
using Xunit;

namespace Application.Tests;

public class OrderSyncShopIndexTests
{
    [Fact]
    public void ResolveByFourSellerShopId_ReturnsTrueOwnerAcrossSellers()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var index = new OrderSyncShopIndex(
        [
            (Guid.NewGuid(), ownerA, "244513", "Shop A"),
            (shopB, ownerB, "239310", "BE Off-Road")
        ]);

        var link = index.ResolveByFourSellerShopId(239310);

        Assert.NotNull(link);
        Assert.Equal(shopB, link!.ShopId);
        Assert.Equal(ownerB, link.OwnerId);
        Assert.Equal("BE Off-Road", link.Name);
    }

    [Fact]
    public void ResolveByFourSellerShopId_Unknown_ReturnsNull()
    {
        var index = new OrderSyncShopIndex(
        [
            (Guid.NewGuid(), Guid.NewGuid(), "244513", "Shop A")
        ]);

        Assert.Null(index.ResolveByFourSellerShopId(999999));
        Assert.Null(index.ResolveByFourSellerShopId(null));
        Assert.Null(index.ResolveByFourSellerShopId(0));
    }

    [Fact]
    public void ResolveByNameForOwner_DoesNotCrossOwnerBoundary()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var shopA = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var index = new OrderSyncShopIndex(
        [
            (shopA, ownerA, "1", "Shared Name"),
            (shopB, ownerB, "2", "Shared Name")
        ]);

        // Same display name on B must not be returned when resolving for A.
        var forA = index.ResolveByNameForOwner("Shared Name", ownerA);
        Assert.NotNull(forA);
        Assert.Equal(shopA, forA!.ShopId);
        Assert.Equal(ownerA, forA.OwnerId);

        // Ambiguous within the same owner → null.
        var ambiguous = new OrderSyncShopIndex(
        [
            (Guid.NewGuid(), ownerA, "1", "Dup"),
            (Guid.NewGuid(), ownerA, "2", "Dup")
        ]);
        Assert.Null(ambiguous.ResolveByNameForOwner("Dup", ownerA));

        // Unique name under A resolves; B's unique name is invisible to A.
        var unique = new OrderSyncShopIndex(
        [
            (Guid.NewGuid(), ownerA, "1", "Only On A"),
            (Guid.NewGuid(), ownerB, "2", "Only On B")
        ]);
        var link = unique.ResolveByNameForOwner("Only On A", ownerA);
        Assert.NotNull(link);
        Assert.Equal(ownerA, link!.OwnerId);
        Assert.Null(unique.ResolveByNameForOwner("Only On B", ownerA));
    }

    [Fact]
    public void Resolve_PrefersFourSellerIdOverName()
    {
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();
        var shopB = Guid.NewGuid();
        var index = new OrderSyncShopIndex(
        [
            (Guid.NewGuid(), ownerA, "100", "Local Name"),
            (shopB, ownerB, "239310", "Other Name")
        ]);

        var link = index.Resolve(239310, "Local Name", ownerA);

        Assert.NotNull(link);
        Assert.Equal(shopB, link!.ShopId);
        Assert.Equal(ownerB, link.OwnerId);
    }

    [Fact]
    public void GetShopIdsForOwner_IncludesNameOnlyShops()
    {
        var owner = Guid.NewGuid();
        var withFs = Guid.NewGuid();
        var nameOnly = Guid.NewGuid();
        var index = new OrderSyncShopIndex(
        [
            (withFs, owner, "100", "FS Shop"),
            (nameOnly, owner, null, "Name Only")
        ]);

        var ids = index.GetShopIdsForOwner(owner);
        Assert.Contains(withFs, ids);
        Assert.Contains(nameOnly, ids);
        Assert.Equal(new HashSet<long> { 100 }, index.GetFourSellerShopIdsForOwner(owner));
    }
}
