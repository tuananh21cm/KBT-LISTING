using System.Globalization;

namespace Shared.Helpers;

/// <summary>
/// Canonical timezone contract:
/// <list type="bullet">
/// <item><description>DB / API wire: UTC instants (<c>timestamptz</c> / ISO-8601 with Z).</description></item>
/// <item><description>Business calendar + UI: <c>Asia/Ho_Chi_Minh</c>.</description></item>
/// <item><description>4Seller returned timestamps (createTime/paidTime/...): naive strings that are
/// already UTC — parsed with <see cref="ParseFourSellerNaiveAsUtc"/>.</description></item>
/// <item><description>4Seller order create-date FILTERS follow US business day
/// (<c>America/Los_Angeles</c>) — see <see cref="ToPacificLocalRange"/>.</description></item>
/// </list>
/// </summary>
public static class AppTimeZones
{
    /// <summary>IANA id for Vietnam business calendar.</summary>
    public const string VietnamId = "Asia/Ho_Chi_Minh";

    /// <summary>IANA id for 4Seller wall-clock / date filters.</summary>
    public const string PacificId = "America/Los_Angeles";

    /// <summary>IANA id for US post-office business hours (USPS scan window anchor).</summary>
    public const string EasternId = "America/New_York";

    /// <summary>Resolved Vietnam timezone (IANA or Windows fallback).</summary>
    public static readonly TimeZoneInfo Vietnam = Resolve(VietnamId, "SE Asia Standard Time");

    /// <summary>Resolved US Pacific timezone (IANA or Windows fallback).</summary>
    public static readonly TimeZoneInfo Pacific = Resolve(PacificId, "Pacific Standard Time");

    /// <summary>Resolved US Eastern timezone (IANA or Windows fallback).</summary>
    public static readonly TimeZoneInfo Eastern = Resolve(EasternId, "Eastern Standard Time");

    private static readonly string[] FourSellerTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss.FFF"
    ];

    /// <summary>
    /// Parses a 4Seller naive timestamp string (no offset). 4Seller returns these already in UTC,
    /// so the wall-clock digits are taken as-is and tagged <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    public static DateTime? ParseFourSellerNaiveAsUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        DateTime parsed;
        if (!DateTime.TryParseExact(
                trimmed,
                FourSellerTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed)
            && !DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    /// <summary>
    /// Formats a UTC instant as a naive Pacific wall-clock string for 4Seller payloads.
    /// </summary>
    public static string FormatFourSellerPacific(DateTime utc)
    {
        var utcInstant = EnsureUtc(utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcInstant, Pacific);
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts UTC bounds to Pacific wall-clock (for 4Seller date filters).
    /// </summary>
    public static (DateTime LocalFrom, DateTime LocalTo) ToPacificLocalRange(DateTime fromUtc, DateTime toUtc)
    {
        return (
            TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(fromUtc), Pacific),
            TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(toUtc), Pacific));
    }

    /// <summary>
    /// Converts UTC bounds to Vietnam wall-clock (for logs / business display).
    /// </summary>
    public static (DateTime LocalFrom, DateTime LocalTo) ToVietnamLocalRange(DateTime fromUtc, DateTime toUtc)
    {
        return (
            TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(fromUtc), Vietnam),
            TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(toUtc), Vietnam));
    }

    /// <summary>
    /// UTC inclusive bounds for the last <paramref name="lookbackDays"/> Vietnam calendar days
    /// (including today). Clamped to 1–<paramref name="maxDays"/> (default 31).
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) GetVietnamLookbackUtcRange(
        int lookbackDays,
        int maxDays = 31)
    {
        var upper = Math.Max(1, maxDays);
        var days = Math.Clamp(lookbackDays, 1, upper);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Vietnam);
        var localStart = localNow.Date.AddDays(-(days - 1));
        var localEnd = localNow.Date.AddDays(1).AddSeconds(-1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), Vietnam),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), Vietnam));
    }

    /// <summary>
    /// Inclusive UTC bounds for a single Vietnam calendar day (yyyy-MM-dd).
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) GetVietnamDayUtcRange(DateOnly localDay)
    {
        var localStart = localDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = localDay.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(localStart, Vietnam),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, Vietnam));
    }

    /// <summary>Normalizes a <see cref="DateTime"/> to UTC Kind for conversions.</summary>
    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static TimeZoneInfo Resolve(string ianaId, string windowsId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }
}
