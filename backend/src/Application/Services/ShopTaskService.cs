using Application.DTOs.Shop;
using Application.DTOs.ShopTask;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Dựng bảng nhiệm vụ hệ thống theo shop và ghi nhận kết quả extension.
///
/// <para>Bảng nhiệm vụ cố ý chỉ đọc dữ liệu đã có sẵn — publish_limit trên <see cref="Shop"/>,
/// <see cref="TikTokShopSyncLog"/> trong ngày và <see cref="ShopTaskCompletion"/> — nên tổng cộng
/// vài truy vấn phẳng cho nhiệm vụ + GROUP BY đơn theo shop (DELIVERED/REFUND) + aggregate
/// snapshot Compass mới nhất (<see cref="ListingAnalyticsSnapshot"/> → Sync/Impressions cho UI tối ưu)
/// + <see cref="ListingDelistDaySummary"/> (delistCount/scanned cho chip «Lấy slot»)
/// + cờ ONGOING Product Discount / Flash Deal từ <see cref="ProductDiscountActivity"/>,
/// không phụ thuộc số shop. Việc chấm điểm từng listing chạy sau sync analytics / khi mở panel
/// delist, không chạy lại trên board.</para>
/// </summary>
public class ShopTaskService : IShopTaskService
{
    private readonly IApplicationDbContext _db;
    private readonly ITikTokShopHealthRecordService _healthRecords;

    /// <summary>Creates the service.</summary>
    public ShopTaskService(IApplicationDbContext db, ITikTokShopHealthRecordService healthRecords)
    {
        _db = db;
        _healthRecords = healthRecords;
    }

    /// <inheritdoc />
    public async Task<ShopTaskBoardDto> GetBoardAsync(
        Guid? ownerScope,
        ShopTaskBoardQuery query,
        CancellationToken cancellationToken = default)
    {
        var date = query.Date ?? Today();
        var (fromUtc, toUtc) = AppTimeZones.GetVietnamDayUtcRange(date);

        var shopQuery = _db.Shops.AsNoTracking();

        if (ownerScope is not null)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == ownerScope.Value);
        }
        else if (query.OwnerId is not null)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == query.OwnerId.Value);
        }

        if (query.ShopId is not null)
        {
            shopQuery = shopQuery.Where(s => s.Id == query.ShopId.Value);
        }

        var search = (query.Search ?? string.Empty).Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            shopQuery = shopQuery.Where(s =>
                s.Name.ToLower().Contains(search)
                || (s.ProfileName != null && s.ProfileName.ToLower().Contains(search))
                || (s.ExternalShopId != null && s.ExternalShopId.ToLower().Contains(search)));
        }

        var shops = await shopQuery
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => new ShopRow(
                s.Id,
                s.Name,
                s.ProfileName,
                s.AvatarUrl,
                s.ExternalShopId,
                s.Status,
                s.OwnerId,
                s.Owner.FullName,
                s.Owner.UserName ?? string.Empty,
                s.PublishLimitPublished,
                s.PublishLimitTotal,
                s.AhrScore,
                s.ShopPerformanceScore,
                s.ProductSatisfactionScore,
                s.FulfillmentScore,
                s.CustomerServiceScore))
            .ToListAsync(cancellationToken);

        var definitions = await _db.ShopTaskDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .Select(d => new DefinitionRow(d.Id, d.Name, d.Url, d.Description))
            .ToListAsync(cancellationToken);

        var board = new ShopTaskBoardDto { Date = date };
        if (shops.Count == 0)
        {
            board.Warnings.Add("Không có shop nào khớp bộ lọc.");
            return board;
        }

        var shopIds = shops.Select(s => s.Id).ToList();
        var externalIds = shops
            .Where(s => !string.IsNullOrWhiteSpace(s.ExternalShopId))
            .Select(s => s.ExternalShopId!)
            .Distinct()
            .ToList();

        var syncRows = externalIds.Count == 0
            ? []
            : await _db.TikTokShopSyncLogs
                .AsNoTracking()
                .Where(x => externalIds.Contains(x.ExternalShopId)
                            && x.Status == TikTokShopSyncStatuses.Completed
                            && x.SyncedAt >= fromUtc
                            && x.SyncedAt <= toUtc)
                .GroupBy(x => new { x.ExternalShopId, x.SyncType })
                .Select(g => new SyncRow(
                    g.Key.ExternalShopId,
                    g.Key.SyncType,
                    g.Max(x => x.SyncedAt)))
                .ToListAsync(cancellationToken);

        var completionRows = definitions.Count == 0
            ? []
            : await _db.ShopTaskCompletions
                .AsNoTracking()
                .Where(c => c.TaskDate == date && shopIds.Contains(c.ShopId))
                .Select(c => new CompletionRow(
                    c.ShopId,
                    c.DefinitionId,
                    c.Status,
                    c.CompletedAt,
                    c.ErrorMessage))
                .ToListAsync(cancellationToken);

        var syncByShop = syncRows
            .GroupBy(r => r.ExternalShopId)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var completionByKey = completionRows
            .ToDictionary(r => (r.ShopId, r.DefinitionId));

        if (definitions.Count == 0)
        {
            board.Warnings.Add("Chưa có nhiệm vụ truy cập URL nào được bật.");
        }

        var healthSummaries = await _healthRecords.GetSummariesForShopsAsync(shopIds, cancellationToken);

        var orderCounts = await _db.Orders
            .AsNoTracking()
            .Where(o => o.ShopId != null
                        && shopIds.Contains(o.ShopId.Value)
                        && (o.WorkflowTab == OrderWorkflowTabs.Delivered
                            || o.WorkflowTab == OrderWorkflowTabs.Refund))
            .GroupBy(o => new { o.ShopId, o.WorkflowTab })
            .Select(g => new { g.Key.ShopId, g.Key.WorkflowTab, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var deliveredByShop = orderCounts
            .Where(r => r.WorkflowTab == OrderWorkflowTabs.Delivered && r.ShopId is not null)
            .ToDictionary(r => r.ShopId!.Value, r => r.Count);
        var refundByShop = orderCounts
            .Where(r => r.WorkflowTab == OrderWorkflowTabs.Refund && r.ShopId is not null)
            .ToDictionary(r => r.ShopId!.Value, r => r.Count);

        var analyticsByShop = await LoadLatestAnalyticsByShopAsync(shopIds, cancellationToken);
        var delistByShop = await LoadLatestDelistSummariesByShopAsync(shopIds, cancellationToken);
        var promoFlagsByShop = await LoadOngoingPromotionFlagsByShopAsync(shopIds, cancellationToken);
        var onlineByShop = await SumOnlineSecondsByShopAsync(
            shopIds,
            fromUtc,
            toUtc,
            date,
            cancellationToken);

        foreach (var shop in shops)
        {
            healthSummaries.TryGetValue(shop.Id, out var healthSummary);
            deliveredByShop.TryGetValue(shop.Id, out var deliveredCount);
            refundByShop.TryGetValue(shop.Id, out var refundCount);
            AnalyticsAgg? analytics = null;
            if (analyticsByShop.TryGetValue(shop.Id, out var foundAnalytics))
            {
                analytics = foundAnalytics;
            }

            DelistSummaryAgg? delist = null;
            if (delistByShop.TryGetValue(shop.Id, out var foundDelist))
            {
                delist = foundDelist;
            }

            promoFlagsByShop.TryGetValue(shop.Id, out var promoFlags);

            var dto = BuildShop(
                shop,
                definitions,
                syncByShop,
                completionByKey,
                healthSummary,
                deliveredCount,
                refundCount,
                analytics,
                delist,
                promoFlags ?? new PromotionFlagsAgg(
                    false, null, null, false, null, null));
            dto.TodayOnlineSeconds = onlineByShop.GetValueOrDefault(shop.Id);
            board.Shops.Add(dto);
        }

        board.Summary = Summarize(board.Shops);
        return board;
    }

    /// <inheritdoc />
    public async Task<List<ShopTaskDefinitionDto>> ListDefinitionsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.ShopTaskDefinitions
            .AsNoTracking()
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .Select(d => new ShopTaskDefinitionDto
            {
                Id = d.Id,
                Name = d.Name,
                Url = d.Url,
                UrlMatchPattern = d.UrlMatchPattern,
                Description = d.Description,
                ViewDurationSeconds = d.ViewDurationSeconds,
                SortOrder = d.SortOrder,
                IsActive = d.IsActive,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ShopTaskDefinitionDto> CreateDefinitionAsync(
        SaveShopTaskDefinitionDto dto,
        CancellationToken cancellationToken = default)
    {
        var maxOrder = await _db.ShopTaskDefinitions
            .MaxAsync(d => (int?)d.SortOrder, cancellationToken) ?? -1;

        var entity = new ShopTaskDefinition();
        Apply(entity, dto);
        entity.SortOrder = maxOrder + 1;
        _db.ShopTaskDefinitions.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    /// <inheritdoc />
    public async Task<ShopTaskDefinitionDto> UpdateDefinitionAsync(
        Guid id,
        SaveShopTaskDefinitionDto dto,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ShopTaskDefinitions
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhiệm vụ.");

        var sortOrder = entity.SortOrder;
        Apply(entity, dto);
        entity.SortOrder = sortOrder;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(entity);
    }

    /// <inheritdoc />
    public async Task ReorderDefinitionsAsync(
        ReorderShopTaskDefinitionsDto dto,
        CancellationToken cancellationToken = default)
    {
        if (dto.OrderedIds.Count == 0)
        {
            throw new BusinessException("Danh sách sắp xếp không được để trống.");
        }

        if (dto.OrderedIds.Distinct().Count() != dto.OrderedIds.Count)
        {
            throw new BusinessException("Danh sách sắp xếp không được chứa ID trùng.");
        }

        var definitions = await _db.ShopTaskDefinitions.ToListAsync(cancellationToken);
        if (definitions.Count != dto.OrderedIds.Count
            || definitions.Any(d => !dto.OrderedIds.Contains(d.Id)))
        {
            throw new BusinessException("Danh sách sắp xếp không khớp với nhiệm vụ hiện có.");
        }

        var now = DateTime.UtcNow;
        for (var i = 0; i < dto.OrderedIds.Count; i++)
        {
            var definition = definitions.First(d => d.Id == dto.OrderedIds[i]);
            definition.SortOrder = i;
            definition.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ShopTaskDefinitions
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhiệm vụ.");

        _db.ShopTaskDefinitions.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ShopTaskExtensionPlanDto> GetExtensionPlanAsync(
        Guid ownerId,
        string? externalShopId,
        CancellationToken cancellationToken = default)
    {
        var date = Today();
        var plan = new ShopTaskExtensionPlanDto { Date = date };

        var sellerId = (externalShopId ?? string.Empty).Trim();
        if (sellerId.Length == 0)
        {
            return plan;
        }

        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.ExternalShopId == sellerId)
            .Select(s => new { s.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (shop is null)
        {
            return plan;
        }

        plan.ShopId = shop.Id;

        var definitions = await _db.ShopTaskDefinitions
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ThenBy(d => d.Name)
            .ToListAsync(cancellationToken);

        if (definitions.Count == 0)
        {
            return plan;
        }

        var doneIds = await _db.ShopTaskCompletions
            .AsNoTracking()
            .Where(c => c.ShopId == shop.Id
                        && c.TaskDate == date
                        && c.Status == ShopTaskStatuses.Done)
            .Select(c => c.DefinitionId)
            .ToListAsync(cancellationToken);

        var doneSet = doneIds.ToHashSet();

        plan.Items = definitions
            .Select(d => new ShopTaskExtensionItemDto
            {
                Id = d.Id,
                Name = d.Name,
                Url = d.Url,
                UrlMatchPattern = string.IsNullOrWhiteSpace(d.UrlMatchPattern)
                    ? ShopTaskStatusRules.BuildDefaultMatchPattern(d.Url)
                    : d.UrlMatchPattern.Trim(),
                ViewDurationSeconds = d.ViewDurationSeconds,
                Done = doneSet.Contains(d.Id)
            })
            .ToList();

        return plan;
    }

    /// <inheritdoc />
    public async Task RecordVisitAsync(
        Guid ownerId,
        RecordShopTaskVisitDto dto,
        CancellationToken cancellationToken = default)
    {
        var sellerId = (dto.ExternalShopId ?? string.Empty).Trim();
        if (sellerId.Length == 0)
        {
            throw new BusinessException("Thiếu externalShopId.");
        }

        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId && s.ExternalShopId == sellerId)
            .Select(s => new { s.Id })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessException("Không tìm thấy shop khớp seller id này.");

        var definition = await _db.ShopTaskDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dto.DefinitionId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhiệm vụ.");

        // Không tin trạng thái client gửi lên: chỉ đánh done khi URL đang mở thực sự khớp đích.
        var claimedDone = string.Equals(dto.Status, ShopTaskStatuses.Done, StringComparison.OrdinalIgnoreCase);
        var matched = ShopTaskStatusRules.MatchesTargetUrl(
            dto.VisitedUrl,
            definition.Url,
            definition.UrlMatchPattern);

        var status = claimedDone && matched ? ShopTaskStatuses.Done : ShopTaskStatuses.Failed;
        var error = status == ShopTaskStatuses.Done
            ? null
            : claimedDone
                ? "URL đang mở không khớp nhiệm vụ."
                : Truncate(dto.ErrorMessage, 500) ?? "Extension báo thất bại.";

        var date = Today();
        var existing = await _db.ShopTaskCompletions
            .FirstOrDefaultAsync(
                c => c.ShopId == shop.Id && c.DefinitionId == definition.Id && c.TaskDate == date,
                cancellationToken);

        if (existing is null)
        {
            _db.ShopTaskCompletions.Add(new ShopTaskCompletion
            {
                DefinitionId = definition.Id,
                ShopId = shop.Id,
                OwnerId = ownerId,
                ExternalShopId = sellerId,
                TaskDate = date,
                Status = status,
                VisitedUrl = Truncate(dto.VisitedUrl, 1000),
                ErrorMessage = error,
                CompletedAt = DateTime.UtcNow,
                Source = ShopTaskSources.Extension
            });
        }
        else if (existing.Status != ShopTaskStatuses.Done || status == ShopTaskStatuses.Done)
        {
            // Đã done rồi thì một lần failed muộn không được ghi đè.
            existing.Status = status;
            existing.VisitedUrl = Truncate(dto.VisitedUrl, 1000);
            existing.ErrorMessage = error;
            existing.CompletedAt = DateTime.UtcNow;
            existing.Source = ShopTaskSources.Extension;
        }
        else
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tổng giây online theo shop trong cửa sổ ngày board VN.
    /// Phiên mở: board = hôm nay → end = UTC now; ngày quá khứ → end = LastSeenAt.
    /// </summary>
    private async Task<Dictionary<Guid, long>> SumOnlineSecondsByShopAsync(
        IReadOnlyList<Guid> shopIds,
        DateTime fromUtc,
        DateTime toUtc,
        DateOnly boardDate,
        CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, long>();
        }

        var vnToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AppTimeZones.Vietnam));
        var useUtcNowForOpen = boardDate == vnToday;
        var now = DateTime.UtcNow;

        var rows = await _db.ShopOnlineSessions.AsNoTracking()
            .Where(s => shopIds.Contains(s.ShopId)
                        && s.StartedAt >= fromUtc
                        && s.StartedAt <= toUtc)
            .Select(s => new { s.ShopId, s.StartedAt, s.EndedAt, s.LastSeenAt })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, long>();
        foreach (var row in rows)
        {
            var end = row.EndedAt
                      ?? (useUtcNowForOpen ? now : row.LastSeenAt);
            if (end < row.StartedAt)
            {
                end = row.StartedAt;
            }

            var seconds = (long)(end - row.StartedAt).TotalSeconds;
            if (seconds <= 0)
            {
                continue;
            }

            result[row.ShopId] = result.GetValueOrDefault(row.ShopId) + seconds;
        }

        return result;
    }

    /// <summary>
    /// Snapshot Compass mới nhất theo shop: <c>MAX(SnapshotDate)</c> rồi
    /// SUM metrics + <c>MAX(SyncedAt)</c> trên đúng ngày đó; CTR = clicks ÷ impressions.
    /// </summary>
    private async Task<Dictionary<Guid, AnalyticsAgg>> LoadLatestAnalyticsByShopAsync(
        List<Guid> shopIds,
        CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, AnalyticsAgg>();
        }

        var latestDates = await _db.ListingAnalyticsSnapshots
            .AsNoTracking()
            .Where(x => shopIds.Contains(x.ShopId))
            .GroupBy(x => x.ShopId)
            .Select(g => new { ShopId = g.Key, SnapshotDate = g.Max(x => x.SnapshotDate) })
            .ToListAsync(cancellationToken);

        if (latestDates.Count == 0)
        {
            return new Dictionary<Guid, AnalyticsAgg>();
        }

        var dateByShop = latestDates.ToDictionary(x => x.ShopId, x => x.SnapshotDate);
        var dates = latestDates.Select(x => x.SnapshotDate).Distinct().ToList();
        var analyticsShopIds = latestDates.Select(x => x.ShopId).ToList();

        var dayTotals = await _db.ListingAnalyticsSnapshots
            .AsNoTracking()
            .Where(x => analyticsShopIds.Contains(x.ShopId) && dates.Contains(x.SnapshotDate))
            .GroupBy(x => new { x.ShopId, x.SnapshotDate })
            .Select(g => new
            {
                g.Key.ShopId,
                g.Key.SnapshotDate,
                Gmv = g.Sum(x => x.Gmv),
                Currency = g.Max(x => x.Currency),
                Impressions = g.Sum(x => x.Impressions),
                Clicks = g.Sum(x => x.Clicks),
                AddToCartUnits = g.Sum(x => x.AddToCartUnits ?? 0),
                Orders = g.Sum(x => x.Orders),
                SyncedAt = g.Max(x => x.SyncedAt)
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, AnalyticsAgg>(dayTotals.Count);
        foreach (var row in dayTotals)
        {
            if (!dateByShop.TryGetValue(row.ShopId, out var expected) || row.SnapshotDate != expected)
            {
                continue;
            }

            result[row.ShopId] = new AnalyticsAgg(
                row.SyncedAt,
                row.Gmv,
                string.IsNullOrWhiteSpace(row.Currency) ? "USD" : row.Currency,
                row.Impressions,
                ListingAnalyticsMetrics.CtrFrom(row.Clicks, row.Impressions),
                row.Clicks,
                row.AddToCartUnits,
                row.Orders);
        }

        return result;
    }

    /// <summary>
    /// Latest <see cref="ListingDelistDaySummary"/> per shop (<c>MAX(SnapshotDate)</c>) —
    /// counters written after analytics sync / delist panel.
    /// </summary>
    private async Task<Dictionary<Guid, DelistSummaryAgg>> LoadLatestDelistSummariesByShopAsync(
        List<Guid> shopIds,
        CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, DelistSummaryAgg>();
        }

        var latestDates = await _db.ListingDelistDaySummaries
            .AsNoTracking()
            .Where(x => shopIds.Contains(x.ShopId))
            .GroupBy(x => x.ShopId)
            .Select(g => new { ShopId = g.Key, SnapshotDate = g.Max(x => x.SnapshotDate) })
            .ToListAsync(cancellationToken);

        if (latestDates.Count == 0)
        {
            return new Dictionary<Guid, DelistSummaryAgg>();
        }

        var dateByShop = latestDates.ToDictionary(x => x.ShopId, x => x.SnapshotDate);
        var dates = latestDates.Select(x => x.SnapshotDate).Distinct().ToList();
        var summaryShopIds = latestDates.Select(x => x.ShopId).ToList();

        var rows = await _db.ListingDelistDaySummaries
            .AsNoTracking()
            .Where(x => summaryShopIds.Contains(x.ShopId) && dates.Contains(x.SnapshotDate))
            .Select(x => new
            {
                x.ShopId,
                x.SnapshotDate,
                x.DelistCount,
                x.ReviewCount,
                x.Scanned,
                x.RecommendedDelistCount
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, DelistSummaryAgg>(rows.Count);
        foreach (var row in rows)
        {
            if (!dateByShop.TryGetValue(row.ShopId, out var expected) || row.SnapshotDate != expected)
            {
                continue;
            }

            result[row.ShopId] = new DelistSummaryAgg(
                row.DelistCount,
                row.ReviewCount,
                row.Scanned,
                row.RecommendedDelistCount);
        }

        return result;
    }

    /// <summary>
    /// ONGOING Product Discount / Flash Deal flags per shop from local mirror.
    /// </summary>
    private async Task<Dictionary<Guid, PromotionFlagsAgg>> LoadOngoingPromotionFlagsByShopAsync(
        List<Guid> shopIds,
        CancellationToken cancellationToken)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, PromotionFlagsAgg>();
        }

        const string ongoing = "ONGOING";
        var rows = await _db.ProductDiscountActivities
            .AsNoTracking()
            .Where(x =>
                x.ShopId != null &&
                shopIds.Contains(x.ShopId.Value) &&
                x.PromotionStatus == ongoing)
            .Select(x => new
            {
                ShopId = x.ShopId!.Value,
                x.DiscountType,
                x.BeginTimeUtc,
                x.EndTimeUtc
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, PromotionFlagsAgg>();
        foreach (var row in rows)
        {
            result.TryGetValue(row.ShopId, out var current);
            var isFlash = string.Equals(
                row.DiscountType,
                PromotionDiscountTypes.FlashSale,
                StringComparison.OrdinalIgnoreCase);

            var hasPd = current?.HasProductDiscount ?? false;
            var pdBegin = current?.ProductDiscountBeginTimeUtc;
            var pdEnd = current?.ProductDiscountEndTimeUtc;
            var hasFlash = current?.HasFlashDeal ?? false;
            var flashBegin = current?.FlashDealBeginTimeUtc;
            var flashEnd = current?.FlashDealEndTimeUtc;

            if (isFlash)
            {
                hasFlash = true;
                if (flashEnd is null || row.EndTimeUtc < flashEnd.Value)
                {
                    flashBegin = row.BeginTimeUtc;
                    flashEnd = row.EndTimeUtc;
                }
            }
            else
            {
                hasPd = true;
                if (pdEnd is null || row.EndTimeUtc < pdEnd.Value)
                {
                    pdBegin = row.BeginTimeUtc;
                    pdEnd = row.EndTimeUtc;
                }
            }

            result[row.ShopId] = new PromotionFlagsAgg(
                HasProductDiscount: hasPd,
                ProductDiscountBeginTimeUtc: pdBegin,
                ProductDiscountEndTimeUtc: pdEnd,
                HasFlashDeal: hasFlash,
                FlashDealBeginTimeUtc: flashBegin,
                FlashDealEndTimeUtc: flashEnd);
        }

        return result;
    }

    private static ShopTaskShopDto BuildShop(
        ShopRow shop,
        List<DefinitionRow> definitions,
        Dictionary<string, List<SyncRow>> syncByShop,
        Dictionary<(Guid ShopId, Guid DefinitionId), CompletionRow> completions,
        ShopHealthSummaryDto? healthSummary,
        int deliveredOrderCount,
        int refundOrderCount,
        AnalyticsAgg? analytics,
        DelistSummaryAgg? delist,
        PromotionFlagsAgg promoFlags)
    {
        List<SyncRow> syncRows = [];
        if (!string.IsNullOrWhiteSpace(shop.ExternalShopId))
        {
            syncByShop.TryGetValue(shop.ExternalShopId, out var found);
            syncRows = found ?? [];
        }

        var completedTypes = syncRows.Select(r => r.SyncType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        DateTime? lastSyncedAt = syncRows.Count == 0 ? null : syncRows.Max(r => r.SyncedAt);

        var dto = new ShopTaskShopDto
        {
            ShopId = shop.Id,
            ShopName = shop.Name,
            ProfileName = shop.ProfileName,
            AvatarUrl = shop.AvatarUrl,
            ExternalShopId = shop.ExternalShopId,
            ShopStatus = shop.Status,
            OwnerId = shop.OwnerId,
            OwnerFullName = shop.OwnerFullName,
            OwnerUserName = shop.OwnerUserName,
            PublishLimitPublished = shop.PublishLimitPublished,
            PublishLimitTotal = shop.PublishLimitTotal,
            RemainingSlots = shop.PublishLimitPublished is null || shop.PublishLimitTotal is null
                ? null
                : Math.Max(0, shop.PublishLimitTotal.Value - shop.PublishLimitPublished.Value),
            HealthSummary = healthSummary is
            {
                ActiveViolationCount: 0,
                ActiveWarningCount: 0,
                HandledViolationCount: 0,
                HandledWarningCount: 0
            }
                ? null
                : healthSummary,
            AhrScore = shop.AhrScore,
            ShopPerformanceScore = shop.ShopPerformanceScore,
            ProductSatisfactionScore = shop.ProductSatisfactionScore,
            FulfillmentScore = shop.FulfillmentScore,
            CustomerServiceScore = shop.CustomerServiceScore,
            DeliveredOrderCount = deliveredOrderCount,
            RefundOrderCount = refundOrderCount,
            HasProductDiscount = promoFlags.HasProductDiscount,
            ProductDiscountBeginTimeUtc = promoFlags.ProductDiscountBeginTimeUtc,
            ProductDiscountEndTimeUtc = promoFlags.ProductDiscountEndTimeUtc,
            HasFlashDeal = promoFlags.HasFlashDeal,
            FlashDealBeginTimeUtc = promoFlags.FlashDealBeginTimeUtc,
            FlashDealEndTimeUtc = promoFlags.FlashDealEndTimeUtc,
            DelistCount = delist?.DelistCount,
            ReviewCount = delist?.ReviewCount,
            AnalyticsSyncedAt = analytics?.SyncedAt,
            AnalyticsGmv = analytics?.Gmv,
            AnalyticsCurrency = analytics?.Currency,
            AnalyticsImpressions = analytics?.Impressions,
            AnalyticsCtr = analytics?.Ctr,
            AnalyticsClicks = analytics?.Clicks,
            AnalyticsAddToCartUnits = analytics?.AddToCartUnits,
            AnalyticsOrders = analytics?.Orders
        };

        dto.Tasks.Add(ShopTaskStatusRules.EvaluateSyncTask(
            shop.ExternalShopId,
            completedTypes,
            lastSyncedAt));

        foreach (var definition in definitions)
        {
            completions.TryGetValue((shop.Id, definition.Id), out var completion);
            dto.Tasks.Add(ShopTaskStatusRules.EvaluateUrlVisit(
                definition.Id,
                definition.Name,
                definition.Url,
                definition.Description,
                completion?.Status,
                completion?.CompletedAt,
                completion?.ErrorMessage));
        }

        dto.Tasks.Add(ShopTaskStatusRules.EvaluateListingFill(
            shop.PublishLimitPublished,
            shop.PublishLimitTotal));

        dto.Tasks.Add(ShopTaskStatusRules.EvaluateDelistCleanup(
            shop.PublishLimitPublished,
            shop.PublishLimitTotal,
            delist?.DelistCount,
            delist?.Scanned,
            delist?.RecommendedDelistCount));

        var daily = dto.Tasks.Where(ShopTaskStatusRules.CountsTowardDailyProgress).ToList();
        dto.TotalCount = daily.Count;
        dto.CompletedCount = daily.Count(t => t.Status == ShopTaskStatuses.Done);
        dto.AttentionCount = dto.Tasks.Count(t =>
            t.Status is ShopTaskStatuses.Attention or ShopTaskStatuses.Failed);

        if (healthSummary?.ActiveViolationCount > 0)
        {
            dto.AttentionCount += 1;
        }

        return dto;
    }

    private static ShopTaskSummaryDto Summarize(List<ShopTaskShopDto> shops) =>
        new()
        {
            ShopCount = shops.Count,
            CompletedShopCount = shops.Count(s => s.TotalCount > 0 && s.CompletedCount == s.TotalCount),
            TotalTaskCount = shops.Sum(s => s.TotalCount),
            CompletedTaskCount = shops.Sum(s => s.CompletedCount),
            AttentionTaskCount = shops.Sum(s => s.AttentionCount),
            PublishLimitTotalSum = shops.Sum(s => s.PublishLimitTotal ?? 0),
            PublishLimitPublishedSum = shops.Sum(s => s.PublishLimitPublished ?? 0),
            DelistCountSum = shops.Sum(s => s.DelistCount ?? 0),
            ReviewCountSum = shops.Sum(s => s.ReviewCount ?? 0)
        };

    private static void Apply(ShopTaskDefinition entity, SaveShopTaskDefinitionDto dto)
    {
        entity.Name = dto.Name.Trim();
        entity.Url = dto.Url.Trim();
        entity.UrlMatchPattern = string.IsNullOrWhiteSpace(dto.UrlMatchPattern)
            ? null
            : dto.UrlMatchPattern.Trim();
        entity.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        entity.ViewDurationSeconds = dto.ViewDurationSeconds;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
    }

    private static ShopTaskDefinitionDto Map(ShopTaskDefinition entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Url = entity.Url,
            UrlMatchPattern = entity.UrlMatchPattern,
            Description = entity.Description,
            ViewDurationSeconds = entity.ViewDurationSeconds,
            SortOrder = entity.SortOrder,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt
        };

    private static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AppTimeZones.Vietnam));

    private static string? Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record ShopRow(
        Guid Id,
        string Name,
        string? ProfileName,
        string? AvatarUrl,
        string? ExternalShopId,
        string Status,
        Guid OwnerId,
        string OwnerFullName,
        string OwnerUserName,
        int? PublishLimitPublished,
        int? PublishLimitTotal,
        int? AhrScore,
        decimal? ShopPerformanceScore,
        decimal? ProductSatisfactionScore,
        decimal? FulfillmentScore,
        decimal? CustomerServiceScore);

    private sealed record DefinitionRow(Guid Id, string Name, string Url, string? Description);

    private sealed record SyncRow(string ExternalShopId, string SyncType, DateTime SyncedAt);

    private sealed record AnalyticsAgg(
        DateTime SyncedAt,
        decimal Gmv,
        string Currency,
        long Impressions,
        decimal? Ctr,
        long Clicks,
        long AddToCartUnits,
        int Orders);

    private sealed record DelistSummaryAgg(
        int DelistCount,
        int ReviewCount,
        int Scanned,
        int RecommendedDelistCount);

    private sealed record PromotionFlagsAgg(
        bool HasProductDiscount,
        DateTime? ProductDiscountBeginTimeUtc,
        DateTime? ProductDiscountEndTimeUtc,
        bool HasFlashDeal,
        DateTime? FlashDealBeginTimeUtc,
        DateTime? FlashDealEndTimeUtc);

    private sealed record CompletionRow(
        Guid ShopId,
        Guid DefinitionId,
        string Status,
        DateTime CompletedAt,
        string? ErrorMessage);
}
