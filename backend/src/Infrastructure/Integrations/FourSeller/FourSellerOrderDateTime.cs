using Shared.Helpers;

namespace Infrastructure.Integrations.FourSeller;

/// <summary>
/// Date helpers for order/listing sync.
/// Business lookback windows use Vietnam calendar days (UTC bounds);
/// 4Seller filter payloads use Pacific wall-clock (same absolute window).
/// </summary>
internal static class FourSellerOrderDateTime
{
    internal static readonly TimeZoneInfo Vietnam = AppTimeZones.Vietnam;
    internal static readonly TimeZoneInfo Pacific = AppTimeZones.Pacific;

    /// <summary>
    /// UTC inclusive bounds for the current VN calendar day (00:00:00 .. 23:59:59 local).
    /// </summary>
    internal static (DateTime FromUtc, DateTime ToUtc) GetCurrentVietnamDayUtcRange() =>
        AppTimeZones.GetVietnamLookbackUtcRange(1);

    /// <summary>
    /// UTC inclusive bounds for the last <paramref name="lookbackDays"/> VN calendar days
    /// (including today). Clamped to 1–<paramref name="maxDays"/> (default 31).
    /// </summary>
    internal static (DateTime FromUtc, DateTime ToUtc) GetVietnamLookbackUtcRange(
        int lookbackDays,
        int maxDays = 31) =>
        AppTimeZones.GetVietnamLookbackUtcRange(lookbackDays, maxDays);

    /// <summary>
    /// Converts UTC bounds to Pacific wall-clock for 4Seller <c>dateQueryCreate*</c> / <c>createTime*</c>.
    /// </summary>
    internal static (DateTime LocalFrom, DateTime LocalTo) ToPacificLocalRange(DateTime from, DateTime to) =>
        AppTimeZones.ToPacificLocalRange(from, to);

    /// <summary>
    /// Converts UTC bounds to VN wall-clock (logging / business display).
    /// </summary>
    internal static (DateTime LocalFrom, DateTime LocalTo) ToVietnamLocalRange(DateTime from, DateTime to) =>
        AppTimeZones.ToVietnamLocalRange(from, to);
}
