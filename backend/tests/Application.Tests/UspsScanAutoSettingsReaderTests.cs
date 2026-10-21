using Application.Services;
using Shared.Constants;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Clamping and defaults for the <c>order.usps_scan.*</c> snapshot shared by the hosted loop
/// and the orchestrator.
/// </summary>
public class UspsScanAutoSettingsReaderTests
{
    [Fact]
    public void From_EmptyMap_UsesSeedDefaults()
    {
        var config = UspsScanAutoSettingsReader.From(new Dictionary<string, string>());

        Assert.False(config.AutoEnabled);
        Assert.Equal(UspsScanAutoSettingKeys.DefaultDelayHours, config.DelayHours);
        Assert.Equal(UspsScanAutoSettingKeys.DefaultChunkSize, config.ChunkSize);
        Assert.Equal(UspsScanAutoSettingKeys.DefaultIntervalMinutes, config.IntervalMinutes);
        // Unseeded window must gate rather than push round the clock.
        Assert.True(config.Window.Enabled);
        Assert.True(config.Window.SaturdayEnabled);
        Assert.Equal(new TimeOnly(8, 0), config.Window.WeekdayStart);
        Assert.Equal(new TimeOnly(17, 0), config.Window.WeekdayEnd);
        Assert.Equal(new TimeOnly(12, 0), config.Window.SaturdayEnd);
        Assert.Equal(UspsScanAutoSettingKeys.DefaultCutoffMarginMinutes, config.Window.CutoffMarginMinutes);
    }

    [Fact]
    public void From_OutOfRangeNumbers_AreClamped()
    {
        var config = UspsScanAutoSettingsReader.From(new Dictionary<string, string>
        {
            [UspsScanAutoSettingKeys.DelayHours] = "9999",
            [UspsScanAutoSettingKeys.ChunkSize] = "-5",
            [UspsScanAutoSettingKeys.IntervalMinutes] = "0",
            [UspsScanAutoSettingKeys.CutoffMarginMinutes] = "10000",
        });

        Assert.Equal(168, config.DelayHours);
        Assert.Equal(0, config.ChunkSize);
        Assert.Equal(1, config.IntervalMinutes);
        Assert.Equal(240, config.Window.CutoffMarginMinutes);
    }

    [Fact]
    public void From_GarbageValues_FallBackWithoutThrowing()
    {
        var config = UspsScanAutoSettingsReader.From(new Dictionary<string, string>
        {
            [UspsScanAutoSettingKeys.AutoEnabled] = "yes",
            [UspsScanAutoSettingKeys.DelayHours] = "abc",
            [UspsScanAutoSettingKeys.WindowEnabled] = "",
            [UspsScanAutoSettingKeys.WeekdayStart] = "25:00",
            [UspsScanAutoSettingKeys.SaturdayEnabled] = "nope",
        });

        Assert.False(config.AutoEnabled);
        Assert.Equal(UspsScanAutoSettingKeys.DefaultDelayHours, config.DelayHours);
        Assert.True(config.Window.Enabled);
        Assert.Equal(new TimeOnly(8, 0), config.Window.WeekdayStart);
        Assert.True(config.Window.SaturdayEnabled);
    }

    [Fact]
    public void From_ExplicitValues_AreHonoured()
    {
        var config = UspsScanAutoSettingsReader.From(new Dictionary<string, string>
        {
            [UspsScanAutoSettingKeys.AutoEnabled] = "True",
            [UspsScanAutoSettingKeys.DelayHours] = "6",
            [UspsScanAutoSettingKeys.ChunkSize] = "25",
            [UspsScanAutoSettingKeys.IntervalMinutes] = "20",
            [UspsScanAutoSettingKeys.WindowEnabled] = "FALSE",
            [UspsScanAutoSettingKeys.WeekdayStart] = "09:30",
            [UspsScanAutoSettingKeys.WeekdayEnd] = "16:00",
            [UspsScanAutoSettingKeys.SaturdayEnabled] = "false",
            [UspsScanAutoSettingKeys.SaturdayStart] = "9:00",
            [UspsScanAutoSettingKeys.SaturdayEnd] = "11:30",
            [UspsScanAutoSettingKeys.CutoffMarginMinutes] = "45",
        });

        Assert.True(config.AutoEnabled);
        Assert.Equal(6, config.DelayHours);
        Assert.Equal(25, config.ChunkSize);
        Assert.Equal(20, config.IntervalMinutes);
        Assert.False(config.Window.Enabled);
        Assert.Equal(new TimeOnly(9, 30), config.Window.WeekdayStart);
        Assert.Equal(new TimeOnly(16, 0), config.Window.WeekdayEnd);
        Assert.False(config.Window.SaturdayEnabled);
        Assert.Equal(new TimeOnly(9, 0), config.Window.SaturdayStart);
        Assert.Equal(new TimeOnly(11, 30), config.Window.SaturdayEnd);
        Assert.Equal(45, config.Window.CutoffMarginMinutes);
    }
}
