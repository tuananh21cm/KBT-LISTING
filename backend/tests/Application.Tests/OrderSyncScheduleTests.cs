using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class OrderSyncScheduleTests
{
    [Fact]
    public void ParseNewOrders_UsesDefaults_WhenKeysMissing()
    {
        var config = OrderSyncScheduleConfig.ParseNewOrders(new Dictionary<string, string>());

        Assert.True(config.Enabled);
        Assert.Equal(OrderSyncSettingKeys.DefaultNewIntervalMinutes, config.IntervalMinutes);
        Assert.Equal(OrderSyncSettingKeys.DefaultNewLookbackDays, config.LookbackDays);
        Assert.Equal(OrderSyncSettingKeys.DefaultNewBatchSize, config.BatchSize);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", true)]
    public void ParseNewOrders_Enabled_ParsesTruthyFalsy(string raw, bool expected)
    {
        var config = OrderSyncScheduleConfig.ParseNewOrders(new Dictionary<string, string>
        {
            [OrderSyncSettingKeys.NewEnabled] = raw
        });

        Assert.Equal(expected, config.Enabled);
    }

    [Theory]
    [InlineData("0", OrderSyncSettingKeys.MinIntervalMinutes)]
    [InlineData("3", 3)]
    [InlineData("9999", OrderSyncSettingKeys.MaxIntervalMinutes)]
    [InlineData("abc", OrderSyncSettingKeys.DefaultNewIntervalMinutes)]
    public void ParseNewOrders_Interval_Clamps(string raw, int expected)
    {
        var config = OrderSyncScheduleConfig.ParseNewOrders(new Dictionary<string, string>
        {
            [OrderSyncSettingKeys.NewIntervalMinutes] = raw
        });

        Assert.Equal(expected, config.IntervalMinutes);
    }

    [Theory]
    [InlineData("0", OrderSyncSettingKeys.MinNewLookbackDays)]
    [InlineData("4", 4)]
    [InlineData("99", OrderSyncSettingKeys.MaxNewLookbackDays)]
    public void ParseNewOrders_Lookback_Clamps(string raw, int expected)
    {
        var config = OrderSyncScheduleConfig.ParseNewOrders(new Dictionary<string, string>
        {
            [OrderSyncSettingKeys.NewLookbackDays] = raw
        });

        Assert.Equal(expected, config.LookbackDays);
    }

    [Theory]
    [InlineData("0", OrderSyncSettingKeys.MinBatchSize)]
    [InlineData("20", 20)]
    [InlineData("500", OrderSyncSettingKeys.MaxBatchSize)]
    public void ParseNewOrders_BatchSize_Clamps(string raw, int expected)
    {
        var config = OrderSyncScheduleConfig.ParseNewOrders(new Dictionary<string, string>
        {
            [OrderSyncSettingKeys.NewBatchSize] = raw
        });

        Assert.Equal(expected, config.BatchSize);
    }

    [Fact]
    public void ParseStatus_UsesDefaults_AndExposesIntervalMinutes()
    {
        var config = OrderSyncScheduleConfig.ParseStatus(new Dictionary<string, string>());

        Assert.True(config.Enabled);
        Assert.Equal(OrderSyncSettingKeys.DefaultStatusIntervalHours, config.IntervalHours);
        Assert.Equal(OrderSyncSettingKeys.DefaultStatusIntervalHours * 60, config.IntervalMinutes);
        Assert.Equal(OrderSyncSettingKeys.DefaultStatusLookbackDays, config.LookbackDays);
        Assert.Equal(OrderSyncSettingKeys.DefaultStatusBatchSize, config.BatchSize);
    }

    [Theory]
    [InlineData("0", OrderSyncSettingKeys.MinIntervalHours)]
    [InlineData("12", 12)]
    [InlineData("999", OrderSyncSettingKeys.MaxIntervalHours)]
    public void ParseStatus_IntervalHours_Clamps(string raw, int expected)
    {
        var config = OrderSyncScheduleConfig.ParseStatus(new Dictionary<string, string>
        {
            [OrderSyncSettingKeys.StatusIntervalHours] = raw
        });

        Assert.Equal(expected, config.IntervalHours);
        Assert.Equal(expected * 60, config.IntervalMinutes);
    }

    [Theory]
    [InlineData("0", OrderSyncSettingKeys.MinStatusLookbackDays)]
    [InlineData("30", 30)]
    [InlineData("120", OrderSyncSettingKeys.MaxStatusLookbackDays)]
    public void ParseStatus_Lookback_Clamps(string raw, int expected)
    {
        var config = OrderSyncScheduleConfig.ParseStatus(new Dictionary<string, string>
        {
            [OrderSyncSettingKeys.StatusLookbackDays] = raw
        });

        Assert.Equal(expected, config.LookbackDays);
    }

    [Fact]
    public void IsDue_WhenNeverFinished_IsTrue()
    {
        var now = DateTimeOffset.Parse("2026-09-29T00:00:00Z");
        Assert.True(OrderSyncScheduleConfig.IsDue(null, 5, now));
    }

    [Fact]
    public void IsDue_UsesLastFinished_NotStart()
    {
        var finished = DateTimeOffset.Parse("2026-09-29T00:00:00Z");
        var now = finished.AddMinutes(5);

        Assert.False(OrderSyncScheduleConfig.IsDue(finished, 5, finished.AddMinutes(4)));
        Assert.True(OrderSyncScheduleConfig.IsDue(finished, 5, now));
        Assert.True(OrderSyncScheduleConfig.IsDue(finished, 5, now.AddSeconds(1)));
    }
}
