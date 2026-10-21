using System.Globalization;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Pure helpers for supplier-seller list/detail (period, money filter, SKU attribution).
/// Kept static so Application.Tests can cover them without EF.
/// </summary>
public static class SupplierSellerQuery
{
    /// <summary>Default list page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Hard cap on list page size.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Default product page size on detail.</summary>
    public const int DefaultProductPageSize = 20;

    /// <summary>Hard cap on product page size.</summary>
    public const int MaxProductPageSize = 50;

    /// <summary>Clamp pageCurrent (≥1) and pageSize.</summary>
    public static (int PageCurrent, int PageSize, int Skip) NormalizePaging(
        int pageCurrent,
        int pageSize,
        int defaultSize = DefaultPageSize,
        int maxSize = MaxPageSize)
    {
        var page = pageCurrent < 1 ? 1 : pageCurrent;
        var size = pageSize < 1 ? defaultSize : Math.Min(pageSize, maxSize);
        return (page, size, (page - 1) * size);
    }

    /// <summary>Display title: explicit storeTitle, else parse soldByTips.</summary>
    public static string? DisplayTitle(string? storeTitle, string? soldByTips) =>
        SupplierPdpMetrics.ResolveStoreTitle(storeTitle, soldByTips);

    /// <summary>Case-insensitive contains on title / code / tips.</summary>
    public static bool MatchesSearch(
        string? storeTitle,
        string? storeCode,
        string? soldByTips,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var needle = search.Trim();
        var display = DisplayTitle(storeTitle, soldByTips);
        return ContainsIgnoreCase(storeTitle, needle)
               || ContainsIgnoreCase(storeCode, needle)
               || ContainsIgnoreCase(soldByTips, needle)
               || ContainsIgnoreCase(display, needle);
    }

    /// <summary>Platform filter (trim, case-insensitive exact).</summary>
    public static bool MatchesPlatform(string platform, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return string.Equals(platform.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same cancel/refund rules as dashboard money KPIs.</summary>
    public static bool IsOrderStatusOk(string? platformOrderStatus, string? orderStatus)
    {
        if (platformOrderStatus is "CANCELLED" or "CANCELED" or "REFUND" or "REFUNDED")
        {
            return false;
        }

        if (string.IsNullOrEmpty(platformOrderStatus)
            && orderStatus is "canceled" or "cancelled")
        {
            return false;
        }

        return true;
    }

    /// <summary>Active shop with a receiving-bank card attached.</summary>
    public static bool IsMoneyEligibleShop(string? shopStatus, Guid? receivingBankAccountId) =>
        shopStatus == ShopStatuses.Active && receivingBankAccountId != null;

    /// <summary>UTC half-open range [start, end) for VN calendar window.</summary>
    public static (DateTime RangeStartUtc, DateTime RangeEndUtc, DateOnly FromLocal, DateOnly ToLocal)
        ResolveUtcRange(DateTime utcNow, string? period, string? fromDate, string? toDate)
    {
        var tz = AppTimeZones.Vietnam;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
        var localToday = DateOnly.FromDateTime(localNow.Date);

        DateOnly fromLocal;
        DateOnly toLocal;
        if (TryParseLocalDate(fromDate, out var fromParsed) && TryParseLocalDate(toDate, out var toParsed))
        {
            fromLocal = fromParsed;
            toLocal = toParsed;
            if (toLocal < fromLocal)
            {
                (fromLocal, toLocal) = (toLocal, fromLocal);
            }

            if (toLocal > localToday)
            {
                toLocal = localToday;
            }

            const int maxDays = 366;
            if (toLocal.DayNumber - fromLocal.DayNumber + 1 > maxDays)
            {
                fromLocal = toLocal.AddDays(-(maxDays - 1));
            }
        }
        else
        {
            (fromLocal, toLocal) = ResolvePeriodLocalDates(localToday, NormalizePeriod(period));
        }

        var startUnspec = DateTime.SpecifyKind(fromLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var endUnspec = DateTime.SpecifyKind(toLocal.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(startUnspec, tz),
            TimeZoneInfo.ConvertTimeToUtc(endUnspec, tz),
            fromLocal,
            toLocal);
    }

    /// <summary>Normalize period token; default <c>30d</c>.</summary>
    public static string NormalizePeriod(string? period)
    {
        var value = (period ?? "30d").Trim().ToLowerInvariant();
        return value is "day" or "week" or "month" or "30d" ? value : "30d";
    }

    /// <summary>First-wins map (OwnerId, skuLower) → supplier seller id.</summary>
    public static Dictionary<(Guid OwnerId, string SkuLower), Guid> BuildSkuSellerMap(
        IEnumerable<(Guid OwnerId, string SellerSku, Guid SupplierSellerId)> links)
    {
        var map = new Dictionary<(Guid OwnerId, string SkuLower), Guid>();
        foreach (var (ownerId, sellerSku, supplierSellerId) in links)
        {
            var sku = sellerSku.Trim();
            if (sku.Length == 0)
            {
                continue;
            }

            map.TryAdd((ownerId, sku.ToLowerInvariant()), supplierSellerId);
        }

        return map;
    }

    /// <summary>Aggregate matched order lines by supplier seller.</summary>
    public static Dictionary<Guid, SupplierSellerMoneyAgg> AggregateMoney(
        IEnumerable<SupplierSellerOrderLine> lines,
        IReadOnlyDictionary<(Guid OwnerId, string SkuLower), Guid> skuMap)
    {
        var result = new Dictionary<Guid, SupplierSellerMoneyAgg>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.SellerSku))
            {
                continue;
            }

            var key = (line.OwnerId, line.SellerSku.Trim().ToLowerInvariant());
            if (!skuMap.TryGetValue(key, out var sellerId))
            {
                continue;
            }

            if (!result.TryGetValue(sellerId, out var agg))
            {
                agg = new SupplierSellerMoneyAgg();
                result[sellerId] = agg;
            }

            agg.OrderIds.Add(line.OrderId);
            agg.ItemQty += line.Quantity;
            agg.ItemRevenue += line.ItemPrice * line.Quantity;
        }

        return result;
    }

    private static (DateOnly From, DateOnly To) ResolvePeriodLocalDates(DateOnly localToday, string period) =>
        period switch
        {
            "day" => (localToday, localToday),
            "week" => (StartOfIsoWeek(localToday), localToday),
            "month" => (new DateOnly(localToday.Year, localToday.Month, 1), localToday),
            _ => (localToday.AddDays(-29), localToday)
        };

    private static DateOnly StartOfIsoWeek(DateOnly localDate)
    {
        var dow = (int)localDate.DayOfWeek;
        var daysFromMonday = dow == 0 ? 6 : dow - 1;
        return localDate.AddDays(-daysFromMonday);
    }

    private static bool TryParseLocalDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>In-memory order line used for supplier attribution.</summary>
public readonly record struct SupplierSellerOrderLine(
    Guid OrderId,
    Guid OwnerId,
    string? SellerSku,
    int Quantity,
    decimal ItemPrice);

/// <summary>Per-shop money aggregate.</summary>
public sealed class SupplierSellerMoneyAgg
{
    /// <summary>Distinct matched order ids.</summary>
    public HashSet<Guid> OrderIds { get; } = [];

    /// <summary>Sum of matched quantities.</summary>
    public int ItemQty { get; set; }

    /// <summary>Σ ItemPrice × Quantity.</summary>
    public decimal ItemRevenue { get; set; }

    /// <summary>Distinct order count.</summary>
    public int OrderCount => OrderIds.Count;
}
