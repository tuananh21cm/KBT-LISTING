using System.Text.Json.Nodes;
using Application.Services.Shein;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Nhận diện gói Club / Saver / hàng ảo từ payload SHEIN gốc.
///
/// Đây là nhánh **phá huỷ** duy nhất chạy trên suy đoán về nội dung đơn: kết luận "hàng ảo" làm lần
/// mua bị gỡ khỏi đơn sàn, mất khỏi giá mua và mất dòng chi trên sổ cái. Nên một dấu hiệu quá rộng ở
/// đây không phải là cảnh báo sai vô hại — nó là tiền biến mất.
///
/// Ca hồi quy: <c>virtual_inventory_flag</c> nói SHEIN trữ/giao món hàng thế nào, không nói món đó là
/// gói thành viên. GSU18V24X001CSQ (<c>goods_id 499132132</c>) và GSU18G24X001XC3
/// (<c>goods_id 502358797</c>) đều mang cờ đó cùng <c>order_type=1</c> và đã shipped/delivered kèm mã
/// vận đơn — hàng vật lý — nhưng vẫn bị loại khỏi giá mua ở mọi lượt sync.
/// </summary>
public class SheinMembershipDetectionTests
{
    /// <summary>Một goods row với các khoá cần cho phép nhận diện.</summary>
    private static JsonObject Goods(
        string goodsId,
        string sku,
        string name,
        int? virtualInventoryFlag = null,
        bool? isPrimeGoods = null,
        bool? isXtraGoods = null)
    {
        var row = new JsonObject
        {
            ["goods_id"] = goodsId,
            ["sku_code"] = sku,
            ["product"] = new JsonObject { ["goods_name"] = name }
        };
        if (virtualInventoryFlag.HasValue) row["virtual_inventory_flag"] = virtualInventoryFlag.Value;
        if (isPrimeGoods.HasValue) row["is_prime_goods"] = isPrimeGoods.Value;
        if (isXtraGoods.HasValue) row["is_xtra_goods"] = isXtraGoods.Value;
        return row;
    }

    private static bool Detect(int? orderType, params JsonObject[] goods)
    {
        var order = new JsonObject
        {
            ["billno"] = "GSU18F24400M9VC",
            ["orderGoodsList"] = new JsonArray(goods.Cast<JsonNode?>().ToArray())
        };
        if (orderType.HasValue) order["order_type"] = orderType.Value;

        return SheinOrderNormalizer
            .NormalizeListOrder(order, "https://us.shein.com")
            .IsMembershipOrVirtual == true;
    }

    [Fact]
    public void VirtualInventoryFlag_AloneIsNotAVirtualSignal()
    {
        // Đúng payload của GSU18V24X001CSQ: hàng vật lý, đã giao, mà mang virtual_inventory_flag=1.
        Assert.False(Detect(
            1,
            Goods("499132132", "I5mq7te5tpg3cl", "Áo thun nam", virtualInventoryFlag: 1)));
    }

    [Fact]
    public void PrimeGoodsFlag_IsNotAVirtualSignal()
    {
        // is_prime_goods nói "món này áp giá SHEIN Club". Tài khoản có Club thì một đơn vật lý bình
        // thường mang cờ này ở mọi dòng.
        Assert.False(Detect(
            1,
            Goods("502358797", "I7mqezkys4z1mo", "Váy nữ", isPrimeGoods: true)));
    }

    [Theory]
    // Id catalog hàng ảo đúng 9 chữ số.
    [InlineData("500000433", true)]
    [InlineData("500000983", true)]
    // Hàng thật cũng bắt đầu bằng 50000 — nới độ dài là loại oan một lần mua thật.
    [InlineData("500001", false)]
    [InlineData("500009876543", false)]
    [InlineData("502358797", false)]
    public void GoodsId_MatchesOnlyTheNineDigitVirtualCatalog(string goodsId, bool expected)
    {
        Assert.Equal(expected, Detect(1, Goods(goodsId, "sz2604161644698", "Sản phẩm")));
    }

    [Theory]
    [InlineData(5, true)]  // Quarterly / Club membership
    [InlineData(6, true)]  // SHEIN Saver / xtra
    [InlineData(1, false)]
    [InlineData(3, false)]
    public void OrderType_DecidesWhenItIsPresent(int orderType, bool expected)
    {
        Assert.Equal(
            expected,
            Detect(orderType, Goods("499132132", "I5mq7te5tpg3cl", "Áo thun nam")));
    }

    [Theory]
    [InlineData("VPP-CLUB-01", true)]
    [InlineData("SQN-SAVER-01", true)]
    [InlineData("sz260406164469834064445", false)]
    public void SkuPrefix_CatchesClubAndSaverSkus(string sku, bool expected)
    {
        Assert.Equal(expected, Detect(1, Goods("499132132", sku, "Sản phẩm")));
    }

    [Fact]
    public void ProductName_CatchesMembershipPackages()
    {
        Assert.True(Detect(
            null,
            Goods("499132132", "I5mq7te5tpg3cl", "SHEIN Club Quarterly Membership")));
    }

    [Fact]
    public void XtraGoods_CountsOnlyWhenEveryRowIsXtra()
    {
        // Đơn toàn SKU Saver là một khoản phí, không có hàng để giao.
        Assert.True(Detect(
            null,
            Goods("111", "a", "Saver A", isXtraGoods: true),
            Goods("222", "b", "Saver B", isXtraGoods: true)));

        // Còn một món vật lý thì đơn vẫn phải fulfil — và tiền của nó vẫn là chi phí thật.
        Assert.False(Detect(
            null,
            Goods("111", "a", "Saver A", isXtraGoods: true),
            Goods("499132132", "I5mq7te5tpg3cl", "Áo thun nam")));
    }

    [Fact]
    public void EmptyGoodsList_IsNotTreatedAsVirtual()
    {
        // Payload rút gọn không kèm goods list. "Không biết" phải rơi về "hàng thật": nhánh ngược lại
        // là gỡ khỏi đơn và xoá dòng sổ cái.
        Assert.False(Detect(null));
    }

    [Fact]
    public void ExplicitVirtualOrderFlag_IsHonored()
    {
        var order = new JsonObject
        {
            ["billno"] = "GSU18F24400M9VC",
            ["is_virtual_order"] = 1,
            ["orderGoodsList"] = new JsonArray()
        };

        Assert.True(SheinOrderNormalizer
            .NormalizeListOrder(order, "https://us.shein.com")
            .IsMembershipOrVirtual == true);
    }
}
