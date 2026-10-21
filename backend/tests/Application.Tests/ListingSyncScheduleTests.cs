using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

public class ListingSyncScheduleTests
{
    [Fact]
    public void ParseDraft_UsesDefaults_WhenKeysMissing()
    {
        var config = ListingSyncScheduleConfig.ParseDraft(new Dictionary<string, string>());

        Assert.True(config.Enabled);
        Assert.Equal(ListingSyncSettingKeys.DefaultDraftIntervalMinutes, config.IntervalMinutes);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", true)]
    public void ParseDraft_Enabled_ParsesTruthyFalsy(string raw, bool expected)
    {
        var config = ListingSyncScheduleConfig.ParseDraft(new Dictionary<string, string>
        {
            [ListingSyncSettingKeys.DraftEnabled] = raw
        });

        Assert.Equal(expected, config.Enabled);
    }

    [Theory]
    [InlineData("0", ListingSyncSettingKeys.MinIntervalMinutes)]
    [InlineData("3", 3)]
    [InlineData("9999", ListingSyncSettingKeys.MaxIntervalMinutes)]
    [InlineData("abc", ListingSyncSettingKeys.DefaultDraftIntervalMinutes)]
    public void ParseDraft_Interval_Clamps(string raw, int expected)
    {
        var config = ListingSyncScheduleConfig.ParseDraft(new Dictionary<string, string>
        {
            [ListingSyncSettingKeys.DraftIntervalMinutes] = raw
        });

        Assert.Equal(expected, config.IntervalMinutes);
    }

    [Fact]
    public void ParsePublished_UsesDefaults_WhenKeysMissing()
    {
        var config = ListingSyncScheduleConfig.ParsePublished(new Dictionary<string, string>());

        Assert.True(config.Enabled);
        Assert.Equal(ListingSyncSettingKeys.DefaultPublishedIntervalMinutes, config.IntervalMinutes);
        Assert.Equal(ListingSyncSettingKeys.DefaultPublishedLookbackDays, config.LookbackDays);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", true)]
    public void ParsePublished_Enabled_ParsesTruthyFalsy(string raw, bool expected)
    {
        var config = ListingSyncScheduleConfig.ParsePublished(new Dictionary<string, string>
        {
            [ListingSyncSettingKeys.PublishedEnabled] = raw
        });

        Assert.Equal(expected, config.Enabled);
    }

    [Theory]
    [InlineData("0", ListingSyncSettingKeys.MinIntervalMinutes)]
    [InlineData("45", 45)]
    [InlineData("9999", ListingSyncSettingKeys.MaxIntervalMinutes)]
    [InlineData("abc", ListingSyncSettingKeys.DefaultPublishedIntervalMinutes)]
    public void ParsePublished_Interval_Clamps(string raw, int expected)
    {
        var config = ListingSyncScheduleConfig.ParsePublished(new Dictionary<string, string>
        {
            [ListingSyncSettingKeys.PublishedIntervalMinutes] = raw
        });

        Assert.Equal(expected, config.IntervalMinutes);
    }

    [Theory]
    [InlineData("0", ListingSyncSettingKeys.MinPublishedLookbackDays)]
    [InlineData("10", 10)]
    [InlineData("99", ListingSyncSettingKeys.MaxPublishedLookbackDays)]
    [InlineData("abc", ListingSyncSettingKeys.DefaultPublishedLookbackDays)]
    public void ParsePublished_Lookback_Clamps(string raw, int expected)
    {
        var config = ListingSyncScheduleConfig.ParsePublished(new Dictionary<string, string>
        {
            [ListingSyncSettingKeys.PublishedLookbackDays] = raw
        });

        Assert.Equal(expected, config.LookbackDays);
    }
}
