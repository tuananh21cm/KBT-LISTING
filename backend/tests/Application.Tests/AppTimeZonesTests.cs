using Shared.Helpers;
using Xunit;

namespace Application.Tests;

public class AppTimeZonesTests
{
    [Fact]
    public void ParseFourSellerNaiveAsUtc_TakesNaiveDigitsAsUtc()
    {
        // 4Seller returns naive strings already in UTC — digits are kept as-is and tagged UTC.
        var utc = AppTimeZones.ParseFourSellerNaiveAsUtc("2026-07-21 02:50:58");
        Assert.NotNull(utc);
        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
        Assert.Equal(new DateTime(2026, 7, 21, 2, 50, 58, DateTimeKind.Utc), utc.Value);
    }

    [Fact]
    public void ParseFourSellerNaiveAsUtc_SupportsSlashAndTFormats()
    {
        Assert.Equal(
            new DateTime(2026, 1, 15, 20, 0, 0, DateTimeKind.Utc),
            AppTimeZones.ParseFourSellerNaiveAsUtc("2026/01/15 20:00:00"));
        Assert.Equal(
            new DateTime(2026, 1, 15, 20, 0, 0, DateTimeKind.Utc),
            AppTimeZones.ParseFourSellerNaiveAsUtc("2026-01-15T20:00:00"));
    }

    [Fact]
    public void GetVietnamDayUtcRange_UsesVietnamMidnightBounds()
    {
        // VN day 2026-07-21 = 2026-07-20 17:00Z .. 2026-07-21 16:59:59Z
        var (fromUtc, toUtc) = AppTimeZones.GetVietnamDayUtcRange(new DateOnly(2026, 7, 21));
        Assert.Equal(new DateTime(2026, 7, 20, 17, 0, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 7, 21, 16, 59, 59, DateTimeKind.Utc), toUtc);
    }

    [Fact]
    public void ToPacificLocalRange_FormatsVnBusinessWindowForFourSeller()
    {
        var (fromUtc, toUtc) = AppTimeZones.GetVietnamDayUtcRange(new DateOnly(2026, 7, 21));
        var (localFrom, localTo) = AppTimeZones.ToPacificLocalRange(fromUtc, toUtc);

        // VN 00:00 = Pacific previous day 10:00 PDT; VN 23:59:59 = Pacific 09:59:59 PDT
        Assert.Equal(new DateTime(2026, 7, 20, 10, 0, 0), localFrom);
        Assert.Equal(new DateTime(2026, 7, 21, 9, 59, 59), localTo);
    }

    [Fact]
    public void ParseFourSellerNaiveAsUtc_ReturnsNullForEmpty()
    {
        Assert.Null(AppTimeZones.ParseFourSellerNaiveAsUtc(null));
        Assert.Null(AppTimeZones.ParseFourSellerNaiveAsUtc(""));
        Assert.Null(AppTimeZones.ParseFourSellerNaiveAsUtc("   "));
    }
}
