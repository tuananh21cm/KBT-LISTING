using Application.Services;
using Xunit;

namespace Application.Tests;

public class ListingAutoPublishScheduleCalculatorTests
{
    [Fact]
    public void NoShops_ReturnsIdlePoll()
    {
        var delay = ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            DateTime.UtcNow,
            [],
            defaultIntervalSeconds: 120);

        Assert.Equal(TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.IdlePollSeconds), delay);
    }

    [Fact]
    public void NoEligibleDrafts_ReturnsIdlePoll()
    {
        var shopId = Guid.NewGuid();
        var delay = ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            DateTime.UtcNow,
            [new ListingAutoPublishScheduleCalculator.ShopScheduleRow(shopId, 120, null, HasEligibleDraft: false)],
            defaultIntervalSeconds: 120);

        Assert.Equal(TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.IdlePollSeconds), delay);
    }

    [Fact]
    public void IntervalElapsed_ReturnsMinPoll()
    {
        var shopId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var delay = ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            now,
            [
                new ListingAutoPublishScheduleCalculator.ShopScheduleRow(
                    shopId,
                    120,
                    now.AddSeconds(-200),
                    HasEligibleDraft: true),
            ],
            defaultIntervalSeconds: 120);

        Assert.Equal(TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.MinPollSeconds), delay);
    }

    [Fact]
    public void IntervalNotElapsed_ReturnsRemainingDelay()
    {
        var shopId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var lastPublish = now.AddSeconds(-30);

        var delay = ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            now,
            [
                new ListingAutoPublishScheduleCalculator.ShopScheduleRow(
                    shopId,
                    120,
                    lastPublish,
                    HasEligibleDraft: true),
            ],
            defaultIntervalSeconds: 120);

        var expected = TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.IdlePollSeconds);
        Assert.Equal(expected, delay);
    }

    [Fact]
    public void NeverPublished_ReturnsMinPoll()
    {
        var shopId = Guid.NewGuid();
        var delay = ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            DateTime.UtcNow,
            [new ListingAutoPublishScheduleCalculator.ShopScheduleRow(shopId, 10, null, HasEligibleDraft: true)],
            defaultIntervalSeconds: 120);

        Assert.Equal(TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.MinPollSeconds), delay);
    }

    [Fact]
    public void ClampDelay_RespectsBounds()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(5),
            ListingAutoPublishScheduleCalculator.ClampDelay(TimeSpan.FromSeconds(1)));
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            ListingAutoPublishScheduleCalculator.ClampDelay(TimeSpan.FromSeconds(120)));
        Assert.Equal(
            TimeSpan.FromSeconds(15),
            ListingAutoPublishScheduleCalculator.ClampDelay(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void ShopProcessing_NotCountedAsEligibleForImmediateWake()
    {
        var shopId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var delay = ListingAutoPublishScheduleCalculator.ComputeNextDelay(
            now,
            [
                new ListingAutoPublishScheduleCalculator.ShopScheduleRow(
                    shopId,
                    10,
                    now.AddSeconds(-30),
                    HasEligibleDraft: false),
            ],
            defaultIntervalSeconds: 120);

        Assert.Equal(TimeSpan.FromSeconds(ListingAutoPublishScheduleCalculator.IdlePollSeconds), delay);
    }
}
