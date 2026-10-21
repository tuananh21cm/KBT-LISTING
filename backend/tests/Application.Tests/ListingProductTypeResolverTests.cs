using System.Text.Json.Nodes;
using Application.Services;
using Domain.Entities;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingProductTypeResolverTests
{
    [Fact]
    public void Resolve_HasVariationOne_WithSingleSkuRow_ReturnsMultiSku()
    {
        var raw = JsonNode.Parse(
            """
            {
              "hasVariation": 1,
              "productType": "SingleSku",
              "option1": "{\"100000\":\"Color\"}",
              "option2": "{\"100007\":\"Size\"}",
              "variation": [{ "id": 6234767920950, "option1": "Mint Green", "option2": "0XL" }]
            }
            """) as JsonObject;

        var listing = new Listing
        {
            VariationCount = 1,
            Variations = [new ListingVariation { SellerSku = "sku-1" }]
        };

        var result = ListingSyncService.ResolveListingProductType(listing, raw);

        Assert.Equal(ListingProductTypes.MultiSku, result);
    }

    [Fact]
    public void Resolve_HasVariationZero_ReturnsSingleSku()
    {
        var raw = JsonNode.Parse("""{"hasVariation":0,"variation":[]}""") as JsonObject;
        var listing = new Listing();

        var result = ListingSyncService.ResolveListingProductType(listing, raw);

        Assert.Equal(ListingProductTypes.SingleSku, result);
    }

    [Fact]
    public void Resolve_MissingHasVariation_MultipleVariationRows_ReturnsMultiSku()
    {
        var raw = JsonNode.Parse("""{"variation":[{"id":1},{"id":2}]}""") as JsonObject;
        var listing = new Listing
        {
            VariationCount = 2,
            Variations =
            [
                new ListingVariation(),
                new ListingVariation()
            ]
        };

        var result = ListingSyncService.ResolveListingProductType(listing, raw);

        Assert.Equal(ListingProductTypes.MultiSku, result);
    }

    [Fact]
    public void Resolve_MissingHasVariation_SalePropAxes_ReturnsMultiSku()
    {
        var raw = JsonNode.Parse(
            """
            {
              "option1": "{\"100000\":\"Color\"}",
              "option1Value": "[{\"Mint Green\":\"Mint Green\"}]",
              "option2": "{\"100007\":\"Size\"}",
              "option2Value": "[{\"0XL\":\"0XL\"}]",
              "variation": [{ "id": 1 }]
            }
            """) as JsonObject;

        var listing = new Listing { VariationCount = 1 };

        var result = ListingSyncService.ResolveListingProductType(listing, raw);

        Assert.Equal(ListingProductTypes.MultiSku, result);
    }

    [Fact]
    public void Resolve_NoSignals_FallsBackToProductTypeField()
    {
        var raw = JsonNode.Parse("""{"productType":"MultiSku"}""") as JsonObject;
        var listing = new Listing();

        var result = ListingSyncService.ResolveListingProductType(listing, raw);

        Assert.Equal(ListingProductTypes.MultiSku, result);
    }
}
