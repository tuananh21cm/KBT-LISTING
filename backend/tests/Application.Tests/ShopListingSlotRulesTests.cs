using Application.Services;
using Shared.Constants;
using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class ShopListingSlotRulesTests
{
    [Theory]
    [InlineData(0, null, false)]
    [InlineData(100, null, false)]
    [InlineData(100, 0, false)]
    [InlineData(199, 200, false)]
    [InlineData(200, 200, true)]
    [InlineData(201, 200, true)]
    public void IsFull_OnlyWhenTotalKnownAndOccupiedReached(int occupied, int? total, bool expected) =>
        Assert.Equal(expected, ShopListingSlotRules.IsFull(occupied, total));

    [Theory]
    [InlineData(null, null, 1)]
    [InlineData(181, 200, 182)]
    [InlineData(199, 200, 200)]
    [InlineData(200, 200, 200)]
    [InlineData(0, 0, 1)]
    public void NextPublishedCount_IncrementsAndCapsAtTotal(int? published, int? total, int expected) =>
        Assert.Equal(expected, ShopListingSlotRules.NextPublishedCount(published, total));

    [Fact]
    public void NextVietnamMidnightUtc_IsStartOfNextVnDay()
    {
        // 10:00 UTC = 17:00 VN on 2026-08-28 → next VN midnight is start of 2026-08-29.
        var utcNow = new DateTime(2026, 8, 28, 10, 0, 0, DateTimeKind.Utc);
        var blockedUntil = ShopListingSlotRules.NextVietnamMidnightUtc(utcNow);
        var (fromUtc, _) = AppTimeZones.GetVietnamDayUtcRange(new DateOnly(2026, 8, 29));
        Assert.Equal(fromUtc, blockedUntil);
    }

    [Fact]
    public void IsBlocked_RespectsUntil()
    {
        var now = DateTime.UtcNow;
        Assert.False(ShopListingSlotRules.IsBlocked(null, now));
        Assert.False(ShopListingSlotRules.IsBlocked(now.AddMinutes(-1), now));
        Assert.True(ShopListingSlotRules.IsBlocked(now.AddMinutes(1), now));
    }
}

public class ListingPublishLimitErrorsTests
{
    [Theory]
    [InlineData("【Publish Failed】Listing.Back.Day_Limit", true)]
    [InlineData("Listing.Back.Day_Limit", true)]
    [InlineData("something Day_Limit something", true)]
    [InlineData("Shop đã hết slot listing.", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsDayLimit_DetectsToken(string? message, bool expected) =>
        Assert.Equal(expected, ListingPublishLimitErrors.IsDayLimit(message));

    [Theory]
    [InlineData("【Publish Failed】Listing.Back.Day_Limit", true)]
    [InlineData("Shop đã hết slot listing.", true)]
    [InlineData("Shop đang bị chặn auto publish (Day_Limit).", true)]
    [InlineData("Thiếu SizeChartImageUrl trên supplier.", false)]
    public void IsShopCapacityFailure_DayLimitOrSlot(string message, bool expected) =>
        Assert.Equal(expected, ListingPublishLimitErrors.IsShopCapacityFailure(message));
}
