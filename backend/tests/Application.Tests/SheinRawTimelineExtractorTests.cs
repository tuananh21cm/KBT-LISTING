using System.Text.Json;
using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Fixture lấy nguyên văn từ payload SHEIN gốc đã thu (đơn GSU18G2440023Y8 đã giao và đơn
/// GSU18V24T001X9L mới rời kho), không phải dữ liệu tự nghĩ ra.
/// </summary>
public class SheinRawTimelineExtractorTests
{
    private static string Envelope(string? listItem, string? detail) =>
        JsonSerializer.Serialize(new
        {
            capturedAt = "2026-08-02T08:09:42Z",
            listItem,
            detail
        });

    private const string DeliveredListItem = """
        {
          "billno": "GSU18G2440023Y8",
          "addTime": 1784855671,
          "delivery_time": "1785002706",
          "track_info": { "status": 6, "time": "1785509449", "status_desc": "Have been delivered" },
          "order_package_info_list": [
            { "packageNo": "C26072401360190", "status": 54, "deliveryTime": 1785002706, "signed_time": 1785509449 }
          ]
        }
        """;

    private const string ShippedListItem = """
        {
          "billno": "GSU18V24T001X9L",
          "addTime": 1785207005,
          "delivery_time": 1785393394,
          "track_info": { "status": 3, "time": 1785635100, "status_desc": "Shipped" },
          "order_package_info_list": [
            { "packageNo": "C26072701832249", "status": 16, "deliveryTime": 1785393394, "signed_time": "" }
          ]
        }
        """;

    [Fact]
    public void Extract_Delivered_UsesSignedTime_NotDeliveryTime()
    {
        var result = SheinRawTimelineExtractor.Extract(Envelope(DeliveredListItem, null));

        Assert.Equal(
            new DateTime(2026, 7, 31, 14, 50, 49, DateTimeKind.Utc),
            result.DeliveredAt);
        Assert.Equal(
            new DateTime(2026, 7, 25, 18, 5, 6, DateTimeKind.Utc),
            result.ShippedAt);
        Assert.True(result.DeliveredAt > result.ShippedAt);
    }

    [Fact]
    public void Extract_Shipped_DoesNotInventDeliveredClock()
    {
        var result = SheinRawTimelineExtractor.Extract(Envelope(ShippedListItem, null));

        Assert.Equal(
            new DateTime(2026, 7, 30, 6, 36, 34, DateTimeKind.Utc),
            result.ShippedAt);
        // track_info.time có giá trị nhưng status_desc là "Shipped" nên không phải mốc giao.
        Assert.Null(result.DeliveredAt);
    }

    [Fact]
    public void Extract_ReadsDetailFragmentUnderOrderKey()
    {
        var detail = $$"""{"order": {{DeliveredListItem}} }""";
        var result = SheinRawTimelineExtractor.Extract(Envelope(null, detail));

        Assert.Equal(
            new DateTime(2026, 7, 31, 14, 50, 49, DateTimeKind.Utc),
            result.DeliveredAt);
    }

    [Fact]
    public void Extract_MultiPackage_WaitsForEveryPackageToBeSigned()
    {
        const string partial = """
            {
              "order_package_info_list": [
                { "deliveryTime": 1785002706, "signed_time": 1785509449 },
                { "deliveryTime": 1785089106, "signed_time": 0 }
              ]
            }
            """;

        var result = SheinRawTimelineExtractor.Extract(Envelope(partial, null));

        Assert.Null(result.DeliveredAt);
        // Rời kho tính theo gói sớm nhất.
        Assert.Equal(
            new DateTime(2026, 7, 25, 18, 5, 6, DateTimeKind.Utc),
            result.ShippedAt);
    }

    [Fact]
    public void Extract_MultiPackage_DeliveredIsLastSignedPackage()
    {
        const string all = """
            {
              "order_package_info_list": [
                { "deliveryTime": 1785002706, "signed_time": 1785509449 },
                { "deliveryTime": 1785089106, "signed_time": 1785595849 }
              ]
            }
            """;

        var result = SheinRawTimelineExtractor.Extract(Envelope(all, null));

        Assert.Equal(
            new DateTime(2026, 8, 1, 14, 50, 49, DateTimeKind.Utc),
            result.DeliveredAt);
    }

    /// <summary>
    /// Đơn GSU18K24Y001DNW thật: hai gói rời kho cách nhau một ngày. Trường <c>delivery_time</c> ở
    /// cấp đơn báo gói muộn (23/07 19:49), nhưng đơn đã bắt đầu di chuyển từ gói đầu (22/07 17:42).
    /// </summary>
    [Fact]
    public void Extract_MultiPackage_PrefersEarliestPackageOverOrderLevelDeliveryTime()
    {
        const string twoPackages = """
            {
              "billno": "GSU18K24Y001DNW",
              "delivery_time": 1784836178,
              "order_package_info_list": [
                { "packageNo": "N26072200159275", "deliveryTime": 1784742177, "signed_time": 1784911051 },
                { "packageNo": "C26072202015999", "deliveryTime": 1784836178, "signed_time": 1785342405 }
              ]
            }
            """;

        var result = SheinRawTimelineExtractor.Extract(Envelope(twoPackages, null));

        Assert.Equal(
            new DateTime(2026, 7, 22, 17, 42, 57, DateTimeKind.Utc),
            result.ShippedAt);
        // Giao xong khi gói cuối được ký nhận.
        Assert.Equal(
            new DateTime(2026, 7, 29, 16, 26, 45, DateTimeKind.Utc),
            result.DeliveredAt);
    }

    [Fact]
    public void Extract_TruncatedFragment_DoesNotThrow()
    {
        var truncated = DeliveredListItem[..80];
        var result = SheinRawTimelineExtractor.Extract(Envelope(truncated, null));

        Assert.Null(result.ShippedAt);
        Assert.Null(result.DeliveredAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Extract_HandlesMissingOrBrokenEnvelope(string? envelope)
    {
        var result = SheinRawTimelineExtractor.Extract(envelope);

        Assert.Null(result.ShippedAt);
        Assert.Null(result.DeliveredAt);
    }
}
