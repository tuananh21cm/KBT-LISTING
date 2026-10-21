using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Services;
using Domain.Entities;
using Infrastructure.Integrations.FourSeller;
using Xunit;

namespace Application.Tests;

public class ListingVariationStockCollapseTests
{
    [Fact]
    public void CollapseStockInfoList_ReplacesMultiRowWithSingleWarehouseQty()
    {
        var vObj = new JsonObject
        {
            ["warehouseId"] = "wh-1",
            ["stockInfoList"] = new JsonArray
            {
                new JsonObject { ["warehouse_id"] = "wh-a", ["available_stock"] = 99 },
                new JsonObject { ["warehouse_id"] = "wh-b", ["available_stock"] = 99 }
            },
            ["globalStockInfoList"] = new JsonArray
            {
                new JsonObject { ["warehouse_id"] = "wh-a", ["available_stock"] = 99 },
                new JsonObject { ["warehouse_id"] = "wh-b", ["available_stock"] = 99 }
            }
        };

        ListingSyncService.CollapseStockInfoList(vObj, 99, "wh-1");

        var stock = Assert.IsType<JsonArray>(vObj["stockInfoList"]);
        Assert.Single(stock);
        var row = Assert.IsType<JsonObject>(stock[0]);
        Assert.Equal("wh-1", row["warehouse_id"]!.GetValue<string>());
        Assert.Equal(99, row["available_stock"]!.GetValue<int>());

        var global = Assert.IsType<JsonArray>(vObj["globalStockInfoList"]);
        Assert.Single(global);
        Assert.Equal(99, Assert.IsType<JsonObject>(global[0])["available_stock"]!.GetValue<int>());
    }

    [Fact]
    public void CollapseStockInfoList_WithoutWarehouse_DoesNotInventRows()
    {
        var vObj = new JsonObject();

        ListingSyncService.CollapseStockInfoList(vObj, 99, null);

        Assert.Null(vObj["stockInfoList"]);
    }

    [Fact]
    public void ReadVariationStock_DoesNotSumStockInfoAndGlobalLists()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "stockInfoList": [ { "warehouse_id": "wh-1", "available_stock": 99 } ],
              "globalStockInfoList": [ { "warehouse_id": "wh-1", "available_stock": 99 } ]
            }
            """);

        var qty = FourSellerApiService.ReadVariationStock(doc.RootElement);

        Assert.Equal(99, qty);
    }

    [Fact]
    public void ReadVariationStock_DuplicateSameWarehouse_TakesMaxNotSum()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "stockInfoList": [
                { "warehouse_id": "wh-1", "available_stock": 99 },
                { "warehouse_id": "wh-1", "available_stock": 99 }
              ]
            }
            """);

        var qty = FourSellerApiService.ReadVariationStock(doc.RootElement);

        Assert.Equal(99, qty);
    }

    [Fact]
    public void ReadVariationStock_DistinctWarehouses_SumsOnceEach()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "stockInfoList": [
                { "warehouse_id": "wh-1", "available_stock": 40 },
                { "warehouse_id": "wh-2", "available_stock": 60 }
              ]
            }
            """);

        var qty = FourSellerApiService.ReadVariationStock(doc.RootElement);

        Assert.Equal(100, qty);
    }

    [Fact]
    public void ApplyVariationToJson_EmptyRow_WritesAvailableStockAndStockInfoFromQuantity()
    {
        var vObj = new JsonObject();
        var match = new ListingVariation { Quantity = 45 };

        ListingSyncService.ApplyVariationToJson(vObj, match, new Dictionary<string, int>(), "wh-1");

        Assert.Equal(45, vObj["availableStock"]!.GetValue<decimal>());
        var stock = Assert.IsType<JsonArray>(vObj["stockInfoList"]);
        Assert.Single(stock);
        var row = Assert.IsType<JsonObject>(stock[0]);
        Assert.Equal("wh-1", row["warehouse_id"]!.GetValue<string>());
        Assert.Equal(45, row["available_stock"]!.GetValue<int>());
    }

    [Fact]
    public void SyncWarehouseAcrossStockInfo_DoesNotCollapseToZero_WhenQuantityWasMirrored()
    {
        var vObj = new JsonObject();
        var match = new ListingVariation { Quantity = 45 };
        ListingSyncService.ApplyVariationToJson(vObj, match, new Dictionary<string, int>(), "wh-1");

        var root = new JsonObject
        {
            ["variation"] = new JsonArray { vObj }
        };
        ListingSyncService.SyncWarehouseAcrossStockInfo(root, "wh-1");

        Assert.Equal(45, vObj["availableStock"]!.GetValue<decimal>());
        var stock = Assert.IsType<JsonArray>(vObj["stockInfoList"]);
        Assert.Equal(45, Assert.IsType<JsonObject>(stock[0])["available_stock"]!.GetValue<int>());
    }

    [Fact]
    public void ApplyVariationToJson_MissingAvailableStockKey_StillWritesQuantity()
    {
        var vObj = new JsonObject
        {
            ["sellerSku"] = "SKU-1"
        };
        var match = new ListingVariation { Quantity = 45, SellerSku = "SKU-1" };

        ListingSyncService.ApplyVariationToJson(vObj, match, new Dictionary<string, int>(), "wh-1");

        Assert.Equal(45, vObj["availableStock"]!.GetValue<decimal>());
        var stock = Assert.IsType<JsonArray>(vObj["stockInfoList"]);
        Assert.Equal(45, Assert.IsType<JsonObject>(stock[0])["available_stock"]!.GetValue<int>());
    }

    [Fact]
    public void VariationHasStock_RejectsZeroStockInfoRow()
    {
        var vObj = new JsonObject
        {
            ["stockInfoList"] = new JsonArray
            {
                new JsonObject { ["warehouse_id"] = "wh-1", ["available_stock"] = 0 }
            }
        };

        Assert.False(ListingSyncService.VariationHasStock(vObj));
    }

    [Fact]
    public void VariationHasStock_AcceptsPositiveStockInfoRow()
    {
        var vObj = new JsonObject
        {
            ["stockInfoList"] = new JsonArray
            {
                new JsonObject { ["warehouse_id"] = "wh-1", ["available_stock"] = 45 }
            }
        };

        Assert.True(ListingSyncService.VariationHasStock(vObj));
    }
}
