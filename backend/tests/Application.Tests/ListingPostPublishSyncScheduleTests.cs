using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingPostPublishSyncScheduleTests
{
    [Theory]
    [InlineData(ListingPublishSources.Auto, false)]
    [InlineData("AUTO", false)]
    [InlineData(ListingPublishSources.Manual, true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void PublishMode_SchedulesPostSyncOnlyForNonAuto(string? publishSource, bool expected)
    {
        var actual = ListingSyncService.ShouldSchedulePostPublishSync(publishSource, isPublishMode: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NonPublishMode_AlwaysSchedulesPostSync()
    {
        Assert.True(ListingSyncService.ShouldSchedulePostPublishSync(ListingPublishSources.Auto, isPublishMode: false));
        Assert.True(ListingSyncService.ShouldSchedulePostPublishSync(ListingPublishSources.Manual, isPublishMode: false));
    }
}
