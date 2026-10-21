using Application.DTOs.Listing;
using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishPrepServiceSkuTests
{
    [Fact]
    public void EnsureUniqueSellerSkus_DuplicateSkuDifferentCombo_AssignsUniqueCodes()
    {
        var detail = new ListingDetailDto
        {
            FourSellerListingId = 853914090827,
            Variations =
            [
                Variation("Black", "S", "132151044-I2865ca4tvzp"),
                Variation("White", "M", "132151044-I2865ca4tvzp")
            ]
        };

        CreateService().EnsureUniqueSellerSkus(detail);

        Assert.Equal(2, detail.Variations.Count);
        var skus = detail.Variations.Select(v => v.SellerSku).ToList();
        Assert.Equal(2, skus.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("132151044-I2865ca4tvzp", skus, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureUniqueSellerSkus_DuplicateCombo_KeepsSingleRow()
    {
        var detail = new ListingDetailDto
        {
            FourSellerListingId = 853914090827,
            Variations =
            [
                Variation("Black", "S", "SKU-A", fourSellerSkuId: 11),
                Variation("Black", "S", "SKU-A", fourSellerSkuId: 12)
            ]
        };

        CreateService().EnsureUniqueSellerSkus(detail);

        Assert.Single(detail.Variations);
        Assert.Equal(11, detail.Variations[0].FourSellerSkuId);
        Assert.Equal("SKU-A", detail.Variations[0].SellerSku);
    }

    [Fact]
    public void EnsureUniqueSellerSkus_EmptySkuOnly_FillsWithoutTouchingExisting()
    {
        var detail = new ListingDetailDto
        {
            FourSellerListingId = 853914090827,
            Variations =
            [
                Variation("Black", "S", "SKU-KEEP"),
                Variation("White", "M", sellerSku: null)
            ]
        };

        CreateService().EnsureUniqueSellerSkus(detail);

        Assert.Equal("SKU-KEEP", detail.Variations[0].SellerSku);
        Assert.False(string.IsNullOrWhiteSpace(detail.Variations[1].SellerSku));
        Assert.NotEqual(detail.Variations[0].SellerSku, detail.Variations[1].SellerSku);
    }

    [Fact]
    public void EnsureUniqueSellerSkus_RegeneratedSku_ClearsFourSellerSkuId()
    {
        var detail = new ListingDetailDto
        {
            FourSellerListingId = 853914090827,
            Variations =
            [
                Variation("Black", "S", "DUP", fourSellerSkuId: 1),
                Variation("White", "M", "DUP", fourSellerSkuId: 2)
            ]
        };

        CreateService().EnsureUniqueSellerSkus(detail);

        var regenerated = detail.Variations.Single(v =>
            !string.Equals(v.SellerSku, "DUP", StringComparison.OrdinalIgnoreCase));
        Assert.Null(regenerated.FourSellerSkuId);
    }

    [Fact]
    public void ShouldClearIncomingSellerSku_WhenAlreadyUsed_ReturnsTrue()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DUP" };

        Assert.True(ListingSellerSkuNormalizer.ShouldClearIncomingSellerSku("DUP", used));
        Assert.False(ListingSellerSkuNormalizer.ShouldClearIncomingSellerSku("NEW", used));
    }

    private static ListingVariationDto Variation(
        string color,
        string size,
        string? sellerSku,
        long? fourSellerSkuId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            FourSellerSkuId = fourSellerSkuId,
            SellerSku = sellerSku,
            Title = $"{color}/{size}",
            VariantAttributes = $$"""{"Color":"{{color}}","Size":"{{size}}"}"""
        };

    private static ListingAutoPublishPrepService CreateService() =>
        new(
            new StubPricingTemplateService(),
            new StubShippingTemplateService(),
            new StubListingSyncService(new ListingDetailDto()),
            new StubHttpClientFactory());
}
