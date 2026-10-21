using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Builds shop context from orders/listings and asks OpenAI for analysis + Shein keywords.
/// </summary>
public class WhatToSellTodayService : IWhatToSellTodayService
{
    private const int MinOrderLinesForWeek = 5;
    private const int TopProductLimit = 15;
    private const int ListingSampleLimit = 20;
    private const int NoSalesScanLimit = 80;
    private const int NoSalesLimit = 12;
    private const int DefaultMinPrice = 8;
    private const int DefaultMaxPrice = 10;
    private const int TrendKeywordCount = 2;
    private const int UpcomingKeywordCount = 3;
    private const int MaxModelAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IApplicationDbContext _db;
    private readonly IOpenAiService _openAi;
    private readonly ILogger<WhatToSellTodayService> _logger;

    /// <summary>Creates the what-to-sell service.</summary>
    public WhatToSellTodayService(
        IApplicationDbContext db,
        IOpenAiService openAi,
        ILogger<WhatToSellTodayService> logger)
    {
        _db = db;
        _openAi = openAi;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WhatToSellTodayDto> AnalyzeAsync(
        Guid shopId,
        Guid? ownerId,
        CancellationToken cancellationToken = default)
    {
        var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == shopId);
        if (ownerId.HasValue)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == ownerId.Value);
        }

        var shop = await shopQuery
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.ProfileName,
                s.Region,
                s.ProductCategories
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var productCategories = ParseProductCategoryLabels(shop.ProductCategories);
        var categoriesMissing = productCategories.Count == 0;

        var nowUtc = DateTime.UtcNow;
        var sales = await LoadBestsellersAsync(shop.Id, nowUtc, cancellationToken);
        var listings = await LoadListingSampleAsync(shop.Id, cancellationToken);
        var noSales = await LoadNoSalesListingsAsync(
            shop.Id,
            sales.SoldSkus,
            sales.SoldTitles,
            cancellationToken);

        var (avgCost, minPrice, maxPrice) = ComputeCostBand(sales.UnitCosts);
        var avgSalePrice = AverageOrNull(sales.Rows.Select(r => r.AvgSalePrice));

        var season = UsRetailCalendar.GetSeason(nowUtc);
        var events = UsRetailCalendar.GetUpcomingEvents(nowUtc);

        var productCountUsed = sales.Rows.Count > 0 ? sales.Rows.Count : listings.Count;
        var asOfDate = nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var context = BuildShopContext(
            shop.Name,
            shop.ProfileName,
            shop.Region,
            productCategories,
            categoriesMissing,
            sales,
            listings,
            noSales,
            avgCost,
            avgSalePrice,
            minPrice,
            maxPrice,
            season,
            events,
            nowUtc);

        var result = await RunModelAsync(context, cancellationToken);

        return new WhatToSellTodayDto
        {
            Analysis = string.Join("\n", result.Points),
            AnalysisPoints = result.Points,
            ProductCategories = productCategories,
            AvgCost = avgCost,
            AvgSalePrice = avgSalePrice,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            PeriodDays = sales.PeriodDays,
            ProductCountUsed = productCountUsed,
            AsOfDate = asOfDate,
            Season = season,
            UpcomingEvents = events.Select(e => e.ToContextLine()).ToList(),
            SearchesTrend = result.Trend,
            SearchesUpcoming = result.Upcoming
        };
    }

    /// <summary>
    /// Calls OpenAI and validates the keyword counts, retrying once when the model
    /// returns fewer than <c>2 + 3</c> usable keywords.
    /// </summary>
    private async Task<ModelResult> RunModelAsync(
        string context,
        CancellationToken cancellationToken)
    {
        ModelResult? best = null;

        for (var attempt = 1; attempt <= MaxModelAttempts; attempt++)
        {
            var raw = await _openAi.CompleteWhatToSellTodayAsync(context, cancellationToken);
            var parsed = ParseModelJson(raw);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var trend = BuildSearches(
                parsed.SearchesTrend ?? parsed.SearchesNow ?? parsed.Searches,
                TrendKeywordCount,
                seen);
            var upcoming = BuildSearches(
                parsed.SearchesUpcoming,
                UpcomingKeywordCount,
                seen);
            var points = NormalizeAnalysisPoints(parsed);

            best = new ModelResult(points, trend, upcoming);
            if (points.Count > 0
                && trend.Count == TrendKeywordCount
                && upcoming.Count == UpcomingKeywordCount)
            {
                return best;
            }

            _logger.LogWarning(
                "WhatToSellToday attempt {Attempt} returned {Trend} trend / {Upcoming} upcoming keywords",
                attempt,
                trend.Count,
                upcoming.Count);
        }

        if (best is null || best.Points.Count == 0)
        {
            throw new BusinessException("OpenAI không trả về đoạn phân tích. Thử lại.");
        }

        throw new BusinessException(
            $"OpenAI không trả đủ {TrendKeywordCount} keyword đang win + {UpcomingKeywordCount} keyword sắp tới. Thử lại.");
    }

    private async Task<SalesSnapshot> LoadBestsellersAsync(
        Guid shopId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var weekLines = await QueryOrderLinesAsync(shopId, nowUtc.AddDays(-7), nowUtc, cancellationToken);
        var periodDays = weekLines.Count >= MinOrderLinesForWeek ? 7 : 30;
        var lines = periodDays == 7
            ? weekLines
            : await QueryOrderLinesAsync(shopId, nowUtc.AddDays(-30), nowUtc, cancellationToken);

        if (lines.Count == 0)
        {
            return new SalesSnapshot(periodDays, [], [], [], []);
        }

        var previousLines = await QueryOrderLinesAsync(
            shopId,
            nowUtc.AddDays(-2 * periodDays),
            nowUtc.AddDays(-periodDays),
            cancellationToken);

        var previousQtyByKey = previousLines
            .GroupBy(GroupKey)
            .ToDictionary(g => g.Key, g => g.Sum(l => NormalizeQty(l.Quantity)));

        var lookups = await LoadSkuLookupsAsync(shopId, lines, cancellationToken);

        var orderQtyById = lines
            .GroupBy(l => l.OrderId)
            .ToDictionary(g => g.Key, g => g.Sum(l => NormalizeQty(l.Quantity)));

        var ranked = lines
            .GroupBy(GroupKey)
            .Select(g =>
            {
                var sellerSku = g
                    .Select(x => x.SellerSku?.Trim())
                    .FirstOrDefault(s => !string.IsNullOrEmpty(s));

                string? category = null;
                if (!string.IsNullOrEmpty(sellerSku))
                {
                    lookups.CategoryBySku.TryGetValue(sellerSku, out category);
                }

                var title = g
                    .Select(x => x.Title?.Trim())
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t))
                    ?? "(untitled)";

                var quantitySold = g.Sum(x => NormalizeQty(x.Quantity));
                var quantityPrev = previousQtyByKey.GetValueOrDefault(g.Key, 0);

                return new RankedProduct(
                    new BestSellerRow(
                        title,
                        category,
                        quantitySold,
                        g.Select(x => x.OrderId).Distinct().Count(),
                        ResolveTrend(quantitySold, quantityPrev),
                        quantityPrev,
                        AverageOrNull(g.Where(x => x.ItemPrice > 0m).Select(x => (decimal?)x.ItemPrice))),
                    g.Select(x => x.OrderId).Distinct().ToList(),
                    sellerSku);
            })
            .OrderByDescending(r => r.Row.QuantitySold)
            .ThenByDescending(r => r.Row.OrderCount)
            .Take(TopProductLimit)
            .ToList();

        var unitCosts = ResolveUnitCosts(ranked, lines, orderQtyById, lookups.InitialPriceBySku);

        var soldSkus = lines
            .Select(l => l.SellerSku?.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var soldTitles = lines
            .Select(l => l.Title?.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new SalesSnapshot(
            periodDays,
            ranked.Select(r => r.Row).ToList(),
            unitCosts,
            soldSkus,
            soldTitles);
    }

    private async Task<List<OrderLine>> QueryOrderLinesAsync(
        Guid shopId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        return await _db.Orders.AsNoTracking()
            .Where(o =>
                o.ShopId == shopId &&
                (o.OrderCreateTime ?? o.CreatedAt) >= fromUtc &&
                (o.OrderCreateTime ?? o.CreatedAt) < toUtc)
            .SelectMany(o => o.Items.Select(i => new OrderLine(
                o.Id,
                o.Cost,
                (decimal?)o.SupplierPurchases.Where(p => p.IsCharged).Sum(p => p.SupplierSubtotal),
                i.SellerSku,
                i.Title,
                i.Quantity,
                i.ItemPrice)))
            .ToListAsync(cancellationToken);
    }

    private async Task<SkuLookups> LoadSkuLookupsAsync(
        Guid shopId,
        IReadOnlyList<OrderLine> lines,
        CancellationToken cancellationToken)
    {
        var categoryBySku = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var initialPriceBySku = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var skuKeys = lines
            .Select(l => l.SellerSku?.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (skuKeys.Count == 0)
        {
            return new SkuLookups(categoryBySku, initialPriceBySku);
        }

        var skuLower = skuKeys.Select(s => s.ToLowerInvariant()).ToList();
        var links = await _db.ListingVariations.AsNoTracking()
            .Where(v =>
                v.SellerSku != null &&
                v.SellerSku != string.Empty &&
                skuLower.Contains(v.SellerSku.ToLower()) &&
                v.Listing != null &&
                v.Listing.ShopId == shopId)
            .Select(v => new
            {
                v.SellerSku,
                v.Listing!.CategoryName,
                v.InitialPrice
            })
            .ToListAsync(cancellationToken);

        foreach (var link in links)
        {
            var sku = link.SellerSku?.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                continue;
            }

            if (!categoryBySku.ContainsKey(sku))
            {
                categoryBySku[sku] = link.CategoryName;
            }

            if (link.InitialPrice is > 0m && !initialPriceBySku.ContainsKey(sku))
            {
                initialPriceBySku[sku] = link.InitialPrice.Value;
            }
        }

        return new SkuLookups(categoryBySku, initialPriceBySku);
    }

    private static List<decimal> ResolveUnitCosts(
        IReadOnlyList<RankedProduct> ranked,
        IReadOnlyList<OrderLine> lines,
        IReadOnlyDictionary<Guid, int> orderQtyById,
        IReadOnlyDictionary<string, decimal> initialPriceBySku)
    {
        var unitCosts = new List<decimal>();
        foreach (var orderId in ranked.SelectMany(r => r.OrderIds).ToHashSet())
        {
            var sample = lines.First(l => l.OrderId == orderId);
            var unit = ResolveUnitGiaNhap(
                sample.Cost,
                sample.SupplierSubtotal,
                orderQtyById.GetValueOrDefault(orderId, 1));
            if (unit is > 0m)
            {
                unitCosts.Add(unit.Value);
            }
        }

        // Fallback: InitialPrice chỉ của SKU nằm trong top bestsellers (không lấy toàn shop).
        if (unitCosts.Count == 0)
        {
            foreach (var item in ranked)
            {
                if (!string.IsNullOrEmpty(item.SellerSku) &&
                    initialPriceBySku.TryGetValue(item.SellerSku, out var price))
                {
                    unitCosts.Add(price);
                }
            }
        }

        return unitCosts;
    }

    /// <summary>
    /// Unit «Giá nhập»: ưu tiên <see cref="Domain.Entities.Order.Cost"/>;
    /// không có thì <c>SupplierSubtotal / qty</c> (cùng công thức sync).
    /// </summary>
    private static decimal? ResolveUnitGiaNhap(decimal? cost, decimal? supplierSubtotal, int orderQty)
    {
        if (cost is > 0m)
        {
            return cost.Value;
        }

        if (supplierSubtotal is > 0m)
        {
            return Math.Round(supplierSubtotal.Value / NormalizeQty(orderQty), 2, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private async Task<List<ListingSampleRow>> LoadListingSampleAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        return await _db.Listings.AsNoTracking()
            .Where(l =>
                l.ShopId == shopId &&
                l.ProductName != null &&
                l.ProductName != string.Empty)
            .OrderByDescending(l => l.UpdatedAt)
            .Take(ListingSampleLimit)
            .Select(l => new ListingSampleRow(l.ProductName!, l.CategoryName))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Active listings with zero order lines in the period — directions already proven weak.
    /// </summary>
    private async Task<List<ListingSampleRow>> LoadNoSalesListingsAsync(
        Guid shopId,
        IReadOnlySet<string> soldSkus,
        IReadOnlySet<string> soldTitles,
        CancellationToken cancellationToken)
    {
        var candidates = await _db.Listings.AsNoTracking()
            .Where(l =>
                l.ShopId == shopId &&
                l.SiteStatus != null &&
                l.SiteStatus.ToLower() == ListingSiteStatuses.Active &&
                l.ProductName != null &&
                l.ProductName != string.Empty)
            .OrderByDescending(l => l.UpdatedAt)
            .Take(NoSalesScanLimit)
            .Select(l => new
            {
                Title = l.ProductName!,
                l.CategoryName,
                Skus = l.Variations
                    .Where(v => v.SellerSku != null && v.SellerSku != string.Empty)
                    .Select(v => v.SellerSku!)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(c =>
                !soldTitles.Contains(c.Title.Trim()) &&
                !c.Skus.Any(s => soldSkus.Contains(s.Trim())))
            .Take(NoSalesLimit)
            .Select(c => new ListingSampleRow(c.Title, c.CategoryName))
            .ToList();
    }

    private static (decimal? AvgCost, int MinPrice, int MaxPrice) ComputeCostBand(
        IReadOnlyList<decimal> unitCosts)
    {
        if (unitCosts.Count == 0)
        {
            return (null, DefaultMinPrice, DefaultMaxPrice);
        }

        var avg = unitCosts.Average();
        var min = Math.Max(1, (int)Math.Floor(avg - 1m));
        var max = Math.Max(min + 1, (int)Math.Ceiling(avg + 1m));
        return (Math.Round(avg, 2), min, max);
    }

    private static string BuildShopContext(
        string shopName,
        string? profileName,
        string? region,
        IReadOnlyList<string> productCategories,
        bool categoriesMissing,
        SalesSnapshot sales,
        IReadOnlyList<ListingSampleRow> listings,
        IReadOnlyList<ListingSampleRow> noSales,
        decimal? avgCost,
        decimal? avgSalePrice,
        int minPrice,
        int maxPrice,
        string season,
        IReadOnlyList<UsRetailEvent> events,
        DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SHOP");
        sb.AppendLine($"name: {shopName}");
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            sb.AppendLine($"profile: {profileName}");
        }

        sb.AppendLine($"region: {(string.IsNullOrWhiteSpace(region) ? "US" : region)}");
        sb.AppendLine($"todayUtc: {nowUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"season: {season}");
        sb.AppendLine(
            categoriesMissing
                ? "productCategories: (none — infer from sold products / listings only)"
                : "productCategories: " + string.Join(", ", productCategories));
        sb.AppendLine("strategy: organic TikTok Shop US search only — no video, no affiliate, no paid ads");
        sb.AppendLine(FormatMoneyLine("avgGiaNhapUsd (supplier cost of top sellers)", avgCost));
        sb.AppendLine(FormatMoneyLine("avgSalePriceUsd (TikTok price customers actually paid)", avgSalePrice));
        sb.AppendLine($"sheinPriceBandUsd: {minPrice}-{maxPrice}");
        sb.AppendLine($"bestsellersPeriodDays: {sales.PeriodDays}");

        sb.AppendLine();
        sb.AppendLine(
            $"UPCOMING_EVENTS (sourcing window {UsRetailCalendar.MinDaysAhead}-{UsRetailCalendar.MaxDaysAhead} days out)");
        if (events.Count == 0)
        {
            sb.AppendLine("(no dated event in window — lean on the season shift)");
        }
        else
        {
            foreach (var item in events)
            {
                sb.AppendLine($"- {item.ToContextLine()}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(
            $"BESTSELLERS (title | category | qtySold | orderCount | trend vs previous {sales.PeriodDays} days | avgSalePriceUsd)");
        if (sales.Rows.Count == 0)
        {
            sb.AppendLine("(none in period)");
        }
        else
        {
            foreach (var row in sales.Rows)
            {
                sb.AppendLine(
                    $"- {row.Title} | {row.Category ?? "-"} | {row.QuantitySold} | {row.OrderCount}"
                    + $" | {row.Trend} (prev {row.QuantityPrev}) | {FormatMoney(row.AvgSalePrice)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("NO_SALES_ACTIVE_LISTINGS (title | category) — live but zero orders in period");
        if (noSales.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var row in noSales)
            {
                sb.AppendLine($"- {row.Title} | {row.Category ?? "-"}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("LISTING_SAMPLE (title | category)");
        if (listings.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            foreach (var row in listings)
            {
                sb.AppendLine($"- {row.Title} | {row.Category ?? "-"}");
            }
        }

        return sb.ToString();
    }

    private ModelResponse ParseModelJson(string raw)
    {
        var json = ExtractJsonObject(raw);
        try
        {
            return JsonSerializer.Deserialize<ModelResponse>(json, JsonOptions)
                ?? throw new BusinessException("OpenAI trả về JSON không hợp lệ.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "WhatToSellToday JSON parse failed: {Snippet}", Truncate(raw, 400));
            throw new BusinessException("OpenAI trả về JSON không hợp lệ.");
        }
    }

    private static List<string> NormalizeAnalysisPoints(ModelResponse parsed)
    {
        var points = new List<string>();
        if (parsed.AnalysisPoints is { Count: > 0 })
        {
            foreach (var p in parsed.AnalysisPoints)
            {
                var t = CleanBullet(p);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    points.Add(t);
                }
            }
        }

        if (points.Count == 0 && !string.IsNullOrWhiteSpace(parsed.Analysis))
        {
            foreach (var line in parsed.Analysis.Replace("\r\n", "\n").Split('\n'))
            {
                var t = CleanBullet(line);
                if (!string.IsNullOrWhiteSpace(t))
                {
                    points.Add(t);
                }
            }
        }

        return points.Take(6).ToList();
    }

    private static string CleanBullet(string? value)
        => (value ?? string.Empty).Trim().TrimStart('-', '•', '*', ' ').Trim();

    private static List<WhatToSellTodaySearchDto> BuildSearches(
        List<ModelSearch>? modelSearches,
        int take,
        HashSet<string> seen)
    {
        var result = new List<WhatToSellTodaySearchDto>();
        if (modelSearches is null)
        {
            return result;
        }

        foreach (var item in modelSearches)
        {
            var keywords = NormalizeKeywords(item.Keywords);
            if (string.IsNullOrWhiteSpace(keywords) || !seen.Add(keywords))
            {
                continue;
            }

            result.Add(new WhatToSellTodaySearchDto
            {
                Keywords = keywords,
                Url = BuildSheinSearchUrl(keywords),
                Rationale = string.IsNullOrWhiteSpace(item.Rationale)
                    ? null
                    : item.Rationale.Trim()
            });

            if (result.Count >= take)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a us.shein.com pdsearch URL shaped like an organic RecentSearch / historyWord
    /// click (no sort/price filters — those trip SHEIN risk/limit).
    /// Also omits <c>src_identifier</c> / <c>src_tab_page_id</c>: SHEIN cross-checks them
    /// against the page-view log of the live session, so a link we mint always claims a
    /// list view that never happened and lands on <c>/risk/action/limit</c>.
    /// Keep in sync with the extension's <c>buildSheinSearchUrl</c>.
    /// </summary>
    public static string BuildSheinSearchUrl(string keywords)
    {
        var kw = keywords.Trim();
        var encoded = Uri.EscapeDataString(kw);
        // SHEIN uses literal backticks as separators in `ici`; only the keyword is %-encoded.
        var ici = $"s1`RecentSearch`{encoded}`_fb`d0`PageOthers";
        return
            $"https://us.shein.com/pdsearch/{encoded}/"
            + $"?ici={ici}"
            + $"&search_source=1"
            + $"&search_type=all"
            + $"&source=historyWord"
            + $"&src_module=search";
    }

    private static string NormalizeKeywords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var cleaned = raw.Trim()
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('/', ' ');
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        // Drop characters that break pdsearch path segments.
        var sb = new StringBuilder(cleaned.Length);
        foreach (var ch in cleaned)
        {
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '&')
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }

    private static string ExtractJsonObject(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl > 0)
            {
                text = text[(firstNl + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new BusinessException("OpenAI không trả về JSON object.");
        }

        return text[start..(end + 1)];
    }

    private static List<string> ParseProductCategoryLabels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ShopProductCategoryDto>>(json, JsonOptions);
            if (parsed is null)
            {
                return [];
            }

            return parsed
                .Select(c => c.Label?.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GroupKey(OrderLine line)
    {
        var sku = line.SellerSku?.Trim();
        if (!string.IsNullOrEmpty(sku))
        {
            return "sku:" + sku.ToUpperInvariant();
        }

        var title = line.Title?.Trim();
        return string.IsNullOrEmpty(title)
            ? "unknown"
            : "title:" + title.ToUpperInvariant();
    }

    private static int NormalizeQty(int quantity) => quantity < 1 ? 1 : quantity;

    /// <summary>Momentum label so the model reads velocity instead of guessing it.</summary>
    private static string ResolveTrend(int current, int previous)
    {
        if (previous <= 0)
        {
            return "NEW";
        }

        if (current >= previous * 1.2)
        {
            return "RISING";
        }

        return current <= previous * 0.8 ? "FALLING" : "FLAT";
    }

    private static decimal? AverageOrNull(IEnumerable<decimal?> values)
    {
        var list = values.Where(v => v is > 0m).Select(v => v!.Value).ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 2);
    }

    private static string FormatMoney(decimal? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatMoneyLine(string label, decimal? value)
        => value.HasValue
            ? $"{label}: {FormatMoney(value)}"
            : $"{label}: (unknown)";

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    private sealed record OrderLine(
        Guid OrderId,
        decimal? Cost,
        decimal? SupplierSubtotal,
        string? SellerSku,
        string? Title,
        int Quantity,
        decimal ItemPrice);

    private sealed record BestSellerRow(
        string Title,
        string? Category,
        int QuantitySold,
        int OrderCount,
        string Trend,
        int QuantityPrev,
        decimal? AvgSalePrice);

    private sealed record RankedProduct(
        BestSellerRow Row,
        List<Guid> OrderIds,
        string? SellerSku);

    private sealed record SalesSnapshot(
        int PeriodDays,
        List<BestSellerRow> Rows,
        List<decimal> UnitCosts,
        HashSet<string> SoldSkus,
        HashSet<string> SoldTitles);

    private sealed record SkuLookups(
        Dictionary<string, string?> CategoryBySku,
        Dictionary<string, decimal> InitialPriceBySku);

    private sealed record ListingSampleRow(string Title, string? Category);

    private sealed record ModelResult(
        List<string> Points,
        List<WhatToSellTodaySearchDto> Trend,
        List<WhatToSellTodaySearchDto> Upcoming);

    private sealed class ModelResponse
    {
        [JsonPropertyName("analysis")]
        public string? Analysis { get; set; }

        [JsonPropertyName("analysisPoints")]
        public List<string>? AnalysisPoints { get; set; }

        /// <summary>Legacy single list (fallback for searchesTrend).</summary>
        [JsonPropertyName("searches")]
        public List<ModelSearch>? Searches { get; set; }

        /// <summary>Legacy name before the trend / upcoming split.</summary>
        [JsonPropertyName("searchesNow")]
        public List<ModelSearch>? SearchesNow { get; set; }

        [JsonPropertyName("searchesTrend")]
        public List<ModelSearch>? SearchesTrend { get; set; }

        [JsonPropertyName("searchesUpcoming")]
        public List<ModelSearch>? SearchesUpcoming { get; set; }
    }

    private sealed class ModelSearch
    {
        [JsonPropertyName("keywords")]
        public string? Keywords { get; set; }

        [JsonPropertyName("rationale")]
        public string? Rationale { get; set; }
    }
}
