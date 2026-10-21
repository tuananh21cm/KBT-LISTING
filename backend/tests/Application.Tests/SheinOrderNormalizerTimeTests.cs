using System.Text.Json.Nodes;
using Application.Services.Shein;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Hợp đồng timezone SHEIN: unix = absolute UTC; 0 = "chưa có".
/// Golden <c>addTime: 1784531136</c> → 2026-07-20 07:05:36Z (= 14:05 VN, không phải Eastern+7 = 10:05).
/// </summary>
public class SheinOrderNormalizerTimeTests
{
    [Fact]
    public void CoerceTime_GoldenAddTime_IsUtcInstant()
    {
        var utc = SheinOrderNormalizer.CoerceTime(JsonValue.Create(1784531136L));

        Assert.NotNull(utc);
        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
        Assert.Equal(new DateTime(2026, 7, 20, 7, 5, 36, DateTimeKind.Utc), utc.Value);
    }

    [Fact]
    public void CoerceTime_NumericString_SameInstant()
    {
        var utc = SheinOrderNormalizer.CoerceTime(JsonValue.Create("1784531136"));

        Assert.Equal(new DateTime(2026, 7, 20, 7, 5, 36, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void CoerceTime_Milliseconds_SameInstant()
    {
        var utc = SheinOrderNormalizer.CoerceTime(JsonValue.Create(1784531136000L));

        Assert.Equal(new DateTime(2026, 7, 20, 7, 5, 36, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void CoerceTime_RejectsZeroAndNegative()
    {
        Assert.Null(SheinOrderNormalizer.CoerceTime(JsonValue.Create(0)));
        Assert.Null(SheinOrderNormalizer.CoerceTime(JsonValue.Create("0")));
        Assert.Null(SheinOrderNormalizer.CoerceTime(JsonValue.Create(-1)));
    }

    [Fact]
    public void ExtractOrderedAt_PrefersAddTime()
    {
        var root = new JsonObject
        {
            ["addTime"] = 1784531136,
            ["pay_time"] = 1784531200
        };

        var orderedAt = SheinOrderNormalizer.ExtractOrderedAt(root);

        Assert.Equal(new DateTime(2026, 7, 20, 7, 5, 36, DateTimeKind.Utc), orderedAt);
    }
}
