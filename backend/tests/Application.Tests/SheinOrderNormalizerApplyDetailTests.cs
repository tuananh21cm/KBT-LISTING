using System.Text.Json.Nodes;
using Application.Services.Shein;
using Xunit;

namespace Application.Tests;

/// <summary>
/// get_order_info envelope unwrap — must match extension <c>detail?.info ?? detail</c>.
/// </summary>
public class SheinOrderNormalizerApplyDetailTests
{
    [Fact]
    public void ApplyDetail_UnwrapsInfoOrderEnvelope_ReadsTotalAndShipping()
    {
        var payload = JsonNode.Parse(
            """
            {
              "code": "0",
              "info": {
                "order": {
                  "billno": "GSU1SC4740020UP",
                  "totalPrice": { "amount": "15.07", "usdAmount": "15.07" },
                  "shippingPrice": { "amount": "3.99", "usdAmount": "3.99" },
                  "subTotalPrice": { "amount": "11.08", "usdAmount": "11.08" },
                  "orderGoodsList": [
                    {
                      "goods_pkg_rel_list": [
                        { "shipping_no": "", "package_no": "N26090800077406" }
                      ]
                    }
                  ],
                  "order_package_info_list": [
                    { "packageNo": "N26090800077406" }
                  ]
                }
              }
            }
            """)!;

        var target = SheinOrderNormalizer.StubOrder("GSU1SC4740020UP");
        SheinOrderNormalizer.ApplyDetail(payload, target);

        Assert.Equal(15.07m, target.Total);
        Assert.Equal(3.99m, target.ShippingFee);
        Assert.Equal(11.08m, target.Subtotal);
    }

    [Fact]
    public void ApplyDetail_BareOrderWithoutEnvelope_StillWorks()
    {
        var payload = JsonNode.Parse(
            """
            {
              "order": {
                "billno": "GSUTEST",
                "totalPrice": { "amount": "10.00", "usdAmount": "10.00" }
              }
            }
            """)!;

        var target = SheinOrderNormalizer.StubOrder("GSUTEST");
        SheinOrderNormalizer.ApplyDetail(payload, target);

        Assert.Equal(10.00m, target.Total);
    }
}
