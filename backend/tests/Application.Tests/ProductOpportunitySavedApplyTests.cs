using Application.Services;
using Xunit;

namespace Application.Tests;

public class ProductOpportunitySavedApplyTests
{
    private static readonly Guid ShopA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ShopB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void BelongsToShop_OtherShopBookmark_IsNotListed()
    {
        Assert.True(ProductOpportunitySavedApply.BelongsToShop(ShopA, ShopA));
        Assert.False(ProductOpportunitySavedApply.BelongsToShop(ShopA, ShopB));
        Assert.False(ProductOpportunitySavedApply.BelongsToShop(null, ShopB));
    }

    [Fact]
    public void IsSavedForShops_ShopBDoesNotInheritShopASave()
    {
        var saved = new HashSet<(Guid ShopId, string Keyword)>
        {
            (ShopA, "midi dress")
        };

        Assert.True(ProductOpportunitySavedApply.IsSavedForShops(saved, [ShopA], "Midi Dress"));
        Assert.False(ProductOpportunitySavedApply.IsSavedForShops(saved, [ShopB], "Midi Dress"));
    }

    [Fact]
    public void ShouldDropBookmark_UnlinkShopA_KeepsShopB()
    {
        var remainingByShop = new HashSet<(Guid ShopId, string Key)>
        {
            (ShopB, "midi dress")
        };

        Assert.True(ProductOpportunitySavedApply.ShouldDropBookmark(ShopA, "midi dress", remainingByShop));
        Assert.False(ProductOpportunitySavedApply.ShouldDropBookmark(ShopB, "midi dress", remainingByShop));
    }

    [Fact]
    public void WithoutSnapshot_KeepsAudienceSuggestBookmarks()
    {
        var saved = new[] { "plus size baby tee", "midi dress", "plus size baby tee" };
        var snapshot = new HashSet<string>(StringComparer.Ordinal) { "midi dress" };

        var kept = ProductOpportunitySavedApply.WithoutSnapshot(
            saved,
            k => k,
            snapshot);

        Assert.Equal(new[] { "plus size baby tee", "plus size baby tee" }, kept);
    }

    [Fact]
    public void WithoutSnapshot_EmptySnapshot_KeepsAll()
    {
        var saved = new[] { "plus size baby tee" };
        var kept = ProductOpportunitySavedApply.WithoutSnapshot(
            saved,
            k => k,
            new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(saved, kept);
    }
}
