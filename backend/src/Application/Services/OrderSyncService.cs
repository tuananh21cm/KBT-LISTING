using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Application.DTOs.FourSeller;
using Application.DTOs.Order;
using Application.DTOs.Shein;
using Application.Interfaces;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;
using Shared.Helpers;
using static Application.Services.SheinMatchKeys;

namespace Application.Services;

/// <summary>
/// Lists local orders and syncs from 4Seller using each seller's cookie.
/// </summary>
public partial class OrderSyncService : IOrderSyncService
{
    /// <summary>
    /// 4Seller bucket trả về mọi trạng thái trộn chung trong một truy vấn phân trang.
    /// </summary>
    internal const string AllStatusBucket = "all";

    /// <summary>
    /// When UI syncs without a status filter (tab Tất cả), one <c>all</c> query per 4Seller
    /// account covers every bucket. Mỗi record tự mang <c>orderStatus</c> nên trang trộn trạng
    /// thái vẫn đóng dấu đúng từng đơn — không cần lặp 6 bucket như trước.
    /// </summary>
    private static readonly string[] DefaultSyncStatuses = [AllStatusBucket];

    /// <summary>
    /// High-volume terminal buckets — keep create-date lookback when
    /// <see cref="OrderSyncRequest.OpenStatusesIgnoreDateFilter"/> is on.
    /// </summary>
    private static readonly HashSet<string> DateFilteredStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "shipped",
        "canceled",
        "cancelled"
    };

    /// <summary>
    /// Hard cap on pages per 4Seller order-page query. Without this a bad <c>total</c> from
    /// 4Seller would loop forever while holding <see cref="FourSellerSyncGate"/>.
    /// Một truy vấn <c>all</c> gánh khối lượng của cả 6 bucket cũ nên cap cao hơn mức 50 trước đây.
    /// </summary>
    private const int MaxPagesPerQuery = 100;

    /// <summary>Max concurrent 4Seller order-detail calls per seller during pricing enrich.</summary>
    private const int PricingDetailConcurrency = 2;

    /// <summary>
    /// Cap detail calls per sync page so first-time backfill cannot turn a 100-order page
    /// into ~50 serial round-trips (manual sync FE timeout is 15 minutes).
    /// Remaining rows retry on the next sync.
    /// </summary>
    private const int PricingDetailMaxPerPage = 40;

    /// <summary>
    /// Tuổi tối đa của đơn sàn còn được nạp vào pool khớp SHEIN (mode "mới").
    /// Sàn tự huỷ đơn chưa ship sau vài ngày nên đơn mở quá hạn này là rác tồn.
    /// </summary>
    private const int SheinMatchPoolMaxAgeDays = 90;

    /// <summary>
    /// Số ngày tối thiểu lùi từ mốc mua SHEIN sớm nhất của lô khi dựng pool mode <c>all</c>.
    /// Đơn sàn đặt <b>trước</b> lần mua (đến hạn ship thường 3–7 ngày); chỉ trừ 3 ngày thì
    /// GSU18L24400NKV4 (mua 23/07) không thấy đơn 577484082473112269 (đặt 19/07) dù phone+zip
    /// trùng tuyệt đối — evidence ghi <c>phoneMatches=0, zipMatches=1</c>.
    /// </summary>
    internal const int SheinMatchPoolMinLeadDays = 14;

    /// <summary>Cache TTL for <see cref="PlatformShippingService"/> rows from 4Seller.</summary>
    private static readonly TimeSpan ShippingServicesCacheTtl = TimeSpan.FromHours(24);


    private readonly IApplicationDbContext _db;
    private readonly IFourSellerApiService _fourSeller;
    private readonly ITelegramNotifier _telegram;
    private readonly ISettingProvider _settings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOrderIssueTagService _issueTags;
    private readonly IFulfilQueueRealtimePublisher _fulfilQueue;
    private readonly ILogger<OrderSyncService> _logger;

    /// <summary>
    /// Active card per purchase account. A sync batch reconciles one ledger row per billno, so
    /// the same account is resolved repeatedly within a single request.
    /// </summary>
    private readonly Dictionary<Guid, Guid?> _activeCardIdByAccount = [];

    /// <summary>
    /// Initializes the order sync service.
    /// </summary>
    public OrderSyncService(
        IApplicationDbContext db,
        IFourSellerApiService fourSeller,
        ITelegramNotifier telegram,
        ISettingProvider settings,
        IServiceScopeFactory scopeFactory,
        IOrderIssueTagService issueTags,
        IFulfilQueueRealtimePublisher fulfilQueue,
        ILogger<OrderSyncService> logger)
    {
        _db = db;
        _fourSeller = fourSeller;
        _telegram = telegram;
        _settings = settings;
        _scopeFactory = scopeFactory;
        _issueTags = issueTags;
        _fulfilQueue = fulfilQueue;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrderListPageDto> ListAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = query.PageCurrent < 1 ? 1 : query.PageCurrent;
        var pageSize = query.PageSize < 1 ? 20 : Math.Min(query.PageSize, 200);

        // Phase 1: page root ids only (no Includes) so Skip/Take is not over a cartesian join.
        var filtered = BuildListQuery(query, includeNav: false);
        var total = await filtered.CountAsync(cancellationToken);
        var sortAsc = string.Equals(query.SortDir, "asc", StringComparison.OrdinalIgnoreCase);

        // PurchasedToday and the PURCHASED workflow tab sort on FulfilPurchasedAt, so the keyset
        // cursor (built on the order-time key) does not apply to them.
        var sortByPurchasedAt = query.PurchasedToday == true
            || string.Equals(
                query.PlatformOrderStatus?.Trim(),
                OrderWorkflowTabs.Purchased,
                StringComparison.OrdinalIgnoreCase);
        // Refund / cancel tabs sort on the platform refund-cancel clock, so the keyset cursor does
        // not apply to them either.
        var terminalSort = sortByPurchasedAt ? null : ResolveTerminalSort(query.PlatformOrderStatus);
        var keysetSort = !sortByPurchasedAt && terminalSort is null;
        var cursor = keysetSort ? OrderListCursor.TryDecode(query.Cursor) : null;

        IOrderedQueryable<Order> ordered;
        if (sortByPurchasedAt)
        {
            // Null stamp last so "just purchased" stays on top of the PURCHASED tab (DESC).
            ordered = sortAsc
                ? filtered
                    .OrderByDescending(o => o.FulfilPurchasedAt.HasValue)
                    .ThenBy(o => o.FulfilPurchasedAt)
                    .ThenBy(o => o.Id)
                : filtered
                    .OrderByDescending(o => o.FulfilPurchasedAt.HasValue)
                    .ThenByDescending(o => o.FulfilPurchasedAt)
                    .ThenBy(o => o.Id);
        }
        else if (terminalSort is { } terminal)
        {
            // Null stamp last: đơn hoàn/hủy chưa có clock sàn không được chen lên đầu. Chỉ luồng
            // sau bán stamp RefundedAt nên gần nửa tab REFUND còn trống — đuôi đó giữ sort theo
            // ngày đặt thay vì rơi về thứ tự Id vô nghĩa.
            ordered = sortAsc
                ? filtered
                    .OrderByDescending(terminal.HasStamp)
                    .ThenBy(terminal.At)
                    .ThenBy(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
                    .ThenBy(o => o.Id)
                : filtered
                    .OrderByDescending(terminal.HasStamp)
                    .ThenByDescending(terminal.At)
                    .ThenByDescending(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
                    .ThenBy(o => o.Id);
        }
        else
        {
            if (cursor is { } seek)
            {
                // Seek past the last row of the previous page instead of counting over it. Ties on
                // the timestamp are broken by Id, matching the sort exactly.
                filtered = sortAsc
                    ? filtered.Where(o =>
                        (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) > seek.TimeKey
                        || ((o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) == seek.TimeKey
                            && o.Id.CompareTo(seek.Id) > 0))
                    : filtered.Where(o =>
                        (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) < seek.TimeKey
                        || ((o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) == seek.TimeKey
                            && o.Id.CompareTo(seek.Id) > 0));
            }

            // Sort on the same key the date filter and PendingQueueFilter use (paid → create →
            // CreatedAt) so one expression index serves filter and sort.
            ordered = sortAsc
                ? filtered
                    .OrderBy(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
                    .ThenBy(o => o.Id)
                : filtered
                    .OrderByDescending(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
                    .ThenBy(o => o.Id);
        }

        var page = ordered
            .Select(o => new PageKey
            {
                Id = o.Id,
                TimeKey = o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt
            });

        // A cursor already skipped the earlier rows; only page numbers need OFFSET.
        var keys = await (cursor is null ? page.Skip((pageCurrent - 1) * pageSize) : page)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var pageIds = keys.Select(k => k.Id).ToList();
        var items = await LoadListPageAsync(pageIds, cancellationToken);

        return new OrderListPageDto
        {
            Items = items,
            Total = total,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            // Only meaningful for the keyset sort, and only when the page was full — a short page
            // is the end of the result set.
            NextCursor = keysetSort && keys.Count == pageSize
                ? OrderListCursor.Encode(keys[^1].TimeKey, keys[^1].Id)
                : null,
            // Counts moved to GET …/orders/status-counts — they never depend on paging or the
            // selected tab, so recomputing them on every page turn was pure waste.
            StatusCounts = null
        };
    }

    /// <summary>Sort position of a listed order, used to build the next keyset cursor.</summary>
    private sealed class PageKey
    {
        public Guid Id { get; init; }
        public DateTime TimeKey { get; init; }
    }

    /// <summary>
    /// Terminal sort key of a refund / cancel tab: the platform clock plus its "stamped" test,
    /// so rows without the clock can be ordered last instead of first (Postgres puts NULL first
    /// on DESC).
    /// </summary>
    private sealed record TerminalSort(
        Expression<Func<Order, DateTime?>> At,
        Expression<Func<Order, bool>> HasStamp);

    /// <summary>
    /// Refund / cancel tabs sort on the platform refund-cancel clock instead of the order-time
    /// key, so the list reads in the same order as 4Seller / TikTok Shop (mới hoàn/hủy lên đầu).
    /// <c>RefundProcessedAt</c> is an ops stamp, not a platform clock, so it is never the sort key.
    /// Every other tab returns <c>null</c> and keeps the order-time sort.
    /// </summary>
    private static TerminalSort? ResolveTerminalSort(string? platformOrderStatus)
        => platformOrderStatus?.Trim().ToUpperInvariant() switch
        {
            OrderWorkflowTabs.Refund
                or OrderWorkflowTabs.RefundUnprocessed
                or OrderWorkflowTabs.RefundProcessed =>
                new TerminalSort(o => o.RefundedAt, o => o.RefundedAt != null),
            OrderWorkflowTabs.Cancelled
                or OrderWorkflowTabs.CancelUnprocessed
                or OrderWorkflowTabs.CancelProcessed =>
                new TerminalSort(o => o.CanceledAt, o => o.CanceledAt != null),
            // Queue gộp hoàn + hủy: đơn hoàn lấy clock hoàn, đơn hủy lấy clock hủy.
            OrderWorkflowTabs.RefundQueueAll
                or OrderWorkflowTabs.RefundQueueUnprocessed
                or OrderWorkflowTabs.RefundQueueProcessed =>
                new TerminalSort(
                    o => o.RefundedAt ?? o.CanceledAt,
                    o => o.RefundedAt != null || o.CanceledAt != null),
            _ => null
        };

    /// <summary>
    /// Whether a search keyword looks like an order / tracking code or a phone number: at least 8
    /// characters and nothing but digits, spaces and the separators codes use.
    ///
    /// Deliberately conservative — anything with a letter goes down the full-column path, because a
    /// tracking number can start with letters and a buyer name never looks like this.
    /// </summary>
    private static bool IsCodeShapedSearch(string search)
    {
        const int MinCodeLength = 8;
        if (search.Length < MinCodeLength)
        {
            return false;
        }

        var digits = 0;
        foreach (var c in search)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
                continue;
            }

            if (c is ' ' or '-' or '_' or '.' or '+' or '(' or ')')
            {
                continue;
            }

            return false;
        }

        return digits >= MinCodeLength;
    }

    /// <summary>
    /// Loads one page of list cards, preserving <paramref name="pageIds"/> order.
    /// Collections come back in separate queries to avoid a cartesian join, and both the
    /// navigations and <c>SupplierPurchases</c> are projected so the list never transfers
    /// <c>RawPayload</c> (~55 KB/row), owner 4Seller cookies or raw card numbers.
    /// </summary>
    private async Task<List<OrderListItemDto>> LoadListPageAsync(
        IReadOnlyList<Guid> pageIds,
        CancellationToken cancellationToken)
    {
        if (pageIds.Count == 0)
        {
            return [];
        }

        var rows = await _db.Orders
            .AsNoTracking()
            .Where(o => pageIds.Contains(o.Id))
            .Select(o => new OrderListRow
            {
                Order = o,
                OwnerFullName = o.Owner!.FullName,
                OwnerUserName = o.Owner!.UserName,
                ShopName = o.Shop!.Name,
                ShopProfileName = o.Shop!.ProfileName,
                FulfilAccountName = o.FulfilAccount!.Name,
                FulfilCardName = o.FulfilCard!.Name,
                FulfilCardNumber = o.FulfilCard!.CardNumber,
                PaidByFullName = o.PaidByUser!.FullName,
                PaidByUserName = o.PaidByUser!.UserName,
                FulfilClaimedByFullName = o.FulfilClaimedByUser!.FullName,
                FulfilClaimedByUserName = o.FulfilClaimedByUser!.UserName
            })
            .ToListAsync(cancellationToken);

        var items = await _db.OrderItems
            .AsNoTracking()
            .Where(i => pageIds.Contains(i.OrderId))
            .ToListAsync(cancellationToken);

        var purchases = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.OrderId != null && pageIds.Contains(p.OrderId.Value))
            .Select(p => new SupplierPurchase
            {
                Id = p.Id,
                OrderId = p.OrderId,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                IsCharged = p.IsCharged,
                Source = p.Source,
                SupplierOrderNo = p.SupplierOrderNo,
                RelationBillno = p.RelationBillno,
                ConfluenceBatchNo = p.ConfluenceBatchNo,
                SupplierOrderUrl = p.SupplierOrderUrl,
                TrackingNo = p.TrackingNo,
                WarehouseTrackingNo = p.WarehouseTrackingNo,
                WarehouseTrackingSeenAt = p.WarehouseTrackingSeenAt,
                Carrier = p.Carrier,
                TrackingKind = p.TrackingKind,
                SupplierTotal = p.SupplierTotal,
                SupplierRetailPrice = p.SupplierRetailPrice,
                SupplierSubtotal = p.SupplierSubtotal,
                SupplierShippingFee = p.SupplierShippingFee,
                SupplierRetailDeliveryFee = p.SupplierRetailDeliveryFee,
                SupplierOnTimeGuarantee = p.SupplierOnTimeGuarantee,
                SupplierCouponPrice = p.SupplierCouponPrice,
                SupplierMembershipDiscount = p.SupplierMembershipDiscount,
                SupplierPointsDiscount = p.SupplierPointsDiscount,
                SupplierWalletPrice = p.SupplierWalletPrice,
                SupplierTax = p.SupplierTax,
                SupplierStatus = p.SupplierStatus,
                SupplierStatusRaw = p.SupplierStatusRaw,
                SupplierStatusAt = p.SupplierStatusAt,
                SupplierUnpaid = p.SupplierUnpaid,
                AutoMarkShipTrackingNo = p.AutoMarkShipTrackingNo,
                AutoMarkShipStatus = p.AutoMarkShipStatus,
                AutoMarkShipAttemptedAt = p.AutoMarkShipAttemptedAt,
                AutoMarkShipError = p.AutoMarkShipError,
                AutoMarkShipRequestId = p.AutoMarkShipRequestId
            })
            .ToListAsync(cancellationToken);

        var itemsByOrder = items
            .GroupBy(i => i.OrderId)
            .ToDictionary(g => g.Key, g => (ICollection<OrderItem>)g.ToList());

        var purchasesByOrder = purchases
            .GroupBy(p => p.OrderId!.Value)
            .ToDictionary(g => g.Key, g => (ICollection<SupplierPurchase>)g.ToList());

        var orders = new List<Order>(rows.Count);
        foreach (var row in rows)
        {
            var order = row.Order;
            order.Items = itemsByOrder.TryGetValue(order.Id, out var list) ? list : [];
            order.SupplierPurchases =
                purchasesByOrder.TryGetValue(order.Id, out var purchaseList) ? purchaseList : [];

            // Rehydrate only the display fields MapListItem reads. Nav stays null when the FK is
            // null so masked-card / name fallbacks behave exactly as with a real Include.
            order.Owner = new ApplicationUser
            {
                FullName = row.OwnerFullName ?? string.Empty,
                UserName = row.OwnerUserName
            };
            order.Shop = order.ShopId is null
                ? null
                : new Shop { Name = row.ShopName ?? string.Empty, ProfileName = row.ShopProfileName };
            order.FulfilAccount = order.FulfilAccountId is null
                ? null
                : new FulfilAccount { Name = row.FulfilAccountName ?? string.Empty };
            order.FulfilCard = order.FulfilCardId is null
                ? null
                : new FulfilCard
                {
                    Name = row.FulfilCardName ?? string.Empty,
                    CardNumber = row.FulfilCardNumber ?? string.Empty
                };
            order.PaidByUser = order.PaidByUserId is null
                ? null
                : new ApplicationUser
                {
                    FullName = row.PaidByFullName ?? string.Empty,
                    UserName = row.PaidByUserName
                };
            order.FulfilClaimedByUser = order.FulfilClaimedByUserId is null
                ? null
                : new ApplicationUser
                {
                    FullName = row.FulfilClaimedByFullName ?? string.Empty,
                    UserName = row.FulfilClaimedByUserName
                };

            orders.Add(order);
        }

        await TryFillOrderItemListingOverlayAsync(orders, cancellationToken);

        var byId = orders.ToDictionary(o => o.Id);
        return pageIds
            .Where(byId.ContainsKey)
            .Select(id => MapListItem(byId[id]))
            .ToList();
    }

    /// <summary>Page row: full order plus the navigation display fields the card needs.</summary>
    private sealed class OrderListRow
    {
        public Order Order { get; init; } = null!;
        public string? OwnerFullName { get; init; }
        public string? OwnerUserName { get; init; }
        public string? ShopName { get; init; }
        public string? ShopProfileName { get; init; }
        public string? FulfilAccountName { get; init; }
        public string? FulfilCardName { get; init; }
        public string? FulfilCardNumber { get; init; }
        public string? PaidByFullName { get; init; }
        public string? PaidByUserName { get; init; }
        public string? FulfilClaimedByFullName { get; init; }
        public string? FulfilClaimedByUserName { get; init; }
    }

    /// <inheritdoc />
    public async Task<OrderStatusCountsDto> GetStatusCountsAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        // Same filters except PlatformOrderStatus (and without paging / nav includes).
        var scopeQuery = new OrderListQuery
        {
            OwnerUserId = query.OwnerUserId,
            Platform = query.Platform,
            ShopId = query.ShopId,
            Search = query.Search,
            DateFrom = query.DateFrom,
            DateTo = query.DateTo
        };

        var baseQ = BuildListQuery(scopeQuery, includeNav: false);

        // The six main tabs are mutually exclusive and now materialized on the row, so one
        // GROUP BY over the denormalized column replaces six separate scans.
        var byTab = await baseQ
            .GroupBy(o => o.WorkflowTab)
            .Select(g => new { Tab = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int TabCount(string tab) =>
            byTab.FirstOrDefault(r => r.Tab == tab)?.Count ?? 0;

        // Ops buckets cross the exclusive tabs (a refunded order can still owe a Thank Card), so
        // they stay conditional counts — one extra scan for all of them.
        var counts = await GroupedCountQuery.ExecuteAsync(
            baseQ,
            [
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.UspsUnprocessed, OrderWorkflowTabFilter.IsUspsUnprocessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.UspsProcessed, OrderWorkflowTabFilter.IsUspsProcessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.ThankCardPending, OrderWorkflowTabFilter.IsThankCardPending),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.ThankCardProcessed, OrderWorkflowTabFilter.IsThankCardProcessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.RefundUnprocessed, OrderWorkflowTabFilter.IsRefundUnprocessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.RefundProcessed, OrderWorkflowTabFilter.IsRefundProcessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.CancelUnprocessed, OrderWorkflowTabFilter.IsCancelUnprocessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.CancelProcessed, OrderWorkflowTabFilter.IsCancelProcessed),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.UspsThankCardPending, OrderWorkflowTabFilter.IsUspsThankCardPending),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.UspsThankCardProcessed, OrderWorkflowTabFilter.IsUspsThankCardProcessed)
            ],
            cancellationToken);

        // Sum of the groups, so a row the backfill has not stamped yet still counts in "Tất cả".
        counts.All = byTab.Sum(r => r.Count);
        counts.PendingProcess = TabCount(OrderWorkflowTabs.PendingProcess);
        counts.Purchased = TabCount(OrderWorkflowTabs.Purchased);
        counts.AwaitingCollection = TabCount(OrderWorkflowTabs.AwaitingCollectionGroup);
        counts.InTransit = TabCount(OrderWorkflowTabs.InTransit);
        counts.Delivered = TabCount(OrderWorkflowTabs.Delivered);
        counts.Canceled = TabCount(OrderWorkflowTabs.Cancelled);
        counts.Refund = TabCount(OrderWorkflowTabs.Refund);

        // Pending queue sub-buckets (within PENDING_PROCESS; ignore active pendingFilter).
        var pendingQ = baseQ.Where(OrderWorkflowTabFilter.IsPendingProcess);
        var pendingCounts = await GroupedCountQuery.ExecuteAsync(
            pendingQ,
            [
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.PendingNew, PendingQueueFilter.PredicateFor(PendingQueueFilter.New)),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.PendingOutStock, PendingQueueFilter.PredicateFor(PendingQueueFilter.OutStock)),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.Pending24h, PendingQueueFilter.PredicateFor(PendingQueueFilter.Hours24)),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.Pending48h, PendingQueueFilter.PredicateFor(PendingQueueFilter.Hours48)),
                GroupedCountQuery.Count<Order, OrderStatusCountsDto>(
                    c => c.PendingOver48h, PendingQueueFilter.PredicateFor(PendingQueueFilter.Over48h))
            ],
            cancellationToken);

        counts.PendingNew = pendingCounts.PendingNew;
        counts.PendingOutStock = pendingCounts.PendingOutStock;
        counts.Pending24h = pendingCounts.Pending24h;
        counts.Pending48h = pendingCounts.Pending48h;
        counts.PendingOver48h = pendingCounts.PendingOver48h;

        // Legacy aliases (sum into chờ xử lý for old clients)
        counts.OnHold = counts.PendingProcess;
        counts.AwaitingShipment = 0;

        return counts;
    }

    /// <inheritdoc />
    public async Task DeleteByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            throw new NotFoundException("Không tìm thấy đơn hàng.");
        }

        _db.Orders.Remove(order);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrderDeleteResultDto> DeleteByFilterAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        // No Include — cascade deletes OrderItems via FK.
        var orders = await BuildListQuery(query, tracking: true).ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return new OrderDeleteResultDto { DeletedCount = 0 };
        }

        _db.Orders.RemoveRange(orders);
        await _db.SaveChangesAsync(cancellationToken);
        return new OrderDeleteResultDto { DeletedCount = orders.Count };
    }

    /// <inheritdoc />
    public async Task<int> RematchOrderShopLinksAsync(
        Guid? ownerUserId = null,
        Guid? shopId = null,
        CancellationToken cancellationToken = default)
    {
        // Single-shop rematch (admin edited FourSellerShopId by hand): keep the owner scope off so
        // foreign-attributed orders are relinked, and narrow the Orders scan to this shop's ids.
        var orderOwnerScope = shopId.HasValue ? null : ownerUserId;

        var shopsQuery = _db.Shops.AsNoTracking().AsQueryable();
        if (shopId.HasValue)
        {
            var targetIds = await ResolveShopFourSellerIdsAsync(shopId.Value, cancellationToken);
            if (targetIds.Count == 0)
            {
                return 0;
            }

            // Rival shops claiming the same id must stay in the index or the ambiguity guard below
            // would silently steal orders that belong to neither.
            shopsQuery = shopsQuery.Where(s =>
                (s.FourSellerShopId != null && targetIds.Contains(s.FourSellerShopId))
                || (s.PreviousFourSellerShopId != null && targetIds.Contains(s.PreviousFourSellerShopId)));
        }
        else if (ownerUserId.HasValue)
        {
            shopsQuery = shopsQuery.Where(s => s.OwnerId == ownerUserId.Value);
        }

        var shops = await shopsQuery
            .Select(s => new { s.Id, s.FourSellerShopId, s.PreviousFourSellerShopId })
            .ToListAsync(cancellationToken);

        if (shops.Count == 0)
        {
            return 0;
        }

        // Index by 4Seller internal id across current + previous reconnect. Only keep fsIds that
        // map to exactly one shop — ambiguous ids are skipped (never First() silently).
        var shopByFourSellerId = new Dictionary<long, Guid>();
        var ambiguousFsIds = new HashSet<long>();

        void IndexFsId(string? raw, Guid candidateShopId)
        {
            if (string.IsNullOrWhiteSpace(raw) || !long.TryParse(raw.Trim(), out var fsId) || fsId <= 0)
            {
                return;
            }

            if (ambiguousFsIds.Contains(fsId))
            {
                return;
            }

            if (shopByFourSellerId.TryGetValue(fsId, out var existing))
            {
                if (existing != candidateShopId)
                {
                    shopByFourSellerId.Remove(fsId);
                    ambiguousFsIds.Add(fsId);
                }

                return;
            }

            shopByFourSellerId[fsId] = candidateShopId;
        }

        foreach (var shop in shops)
        {
            IndexFsId(shop.FourSellerShopId, shop.Id);
            IndexFsId(shop.PreviousFourSellerShopId, shop.Id);
        }

        if (shopByFourSellerId.Count == 0)
        {
            return 0;
        }

        var fsIds = shopByFourSellerId.Keys.ToList();

        IQueryable<Order> ScopeToOwner(IQueryable<Order> q)
        {
            if (!orderOwnerScope.HasValue)
            {
                return q;
            }

            var ownerId = orderOwnerScope.Value;
            return q.Where(o => o.OwnerId == ownerId);
        }

        // One DB-side aggregate decides which 4Seller ids actually carry a wrong/missing ShopId.
        // Steady state returns nothing, so a routine shop sync never pages through Orders.
        var pairs = await ScopeToOwner(
                _db.Orders.Where(o => o.FourSellerShopId.HasValue && fsIds.Contains(o.FourSellerShopId.Value)))
            .GroupBy(o => new { o.FourSellerShopId, o.ShopId })
            .Select(g => new { g.Key.FourSellerShopId, g.Key.ShopId })
            .ToListAsync(cancellationToken);

        var staleFsIds = pairs
            .Where(p => p.FourSellerShopId.HasValue
                && shopByFourSellerId.TryGetValue(p.FourSellerShopId.Value, out var target)
                && p.ShopId != target)
            .Select(p => p.FourSellerShopId!.Value)
            .Distinct()
            .ToList();

        if (staleFsIds.Count == 0)
        {
            return 0;
        }

        var changed = 0;
        var now = DateTime.UtcNow;

        foreach (var fsId in staleFsIds)
        {
            var targetShopId = shopByFourSellerId[fsId];

            // OwnerId is intentionally NOT rewritten — rematch only restores ShopId so mismatched
            // ownership stays visible to ReassignAllMismatchedOwnedDataAsync.
            var orders = await ScopeToOwner(
                    _db.Orders.Where(o =>
                        o.FourSellerShopId == fsId
                        && (o.ShopId == null || o.ShopId != targetShopId)))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                order.ShopId = targetShopId;
                order.UpdatedAt = now;
            }

            changed += orders.Count;
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    /// <summary>
    /// 4Seller ids one shop can legitimately claim: the current one plus the id it carried before
    /// a reconnect (orders synced under the old id must follow the shop).
    /// </summary>
    private async Task<List<string>> ResolveShopFourSellerIdsAsync(
        Guid shopId,
        CancellationToken cancellationToken)
    {
        var shop = await _db.Shops
            .AsNoTracking()
            .Where(s => s.Id == shopId)
            .Select(s => new { s.FourSellerShopId, s.PreviousFourSellerShopId })
            .FirstOrDefaultAsync(cancellationToken);

        if (shop is null)
        {
            return [];
        }

        return new[] { shop.FourSellerShopId, shop.PreviousFourSellerShopId }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private IQueryable<Order> BuildListQuery(
        OrderListQuery query,
        bool tracking = false,
        bool includeNav = true)
    {
        IQueryable<Order> q = tracking
            ? _db.Orders
            : _db.Orders.AsNoTracking();

        if (!tracking && includeNav)
        {
            q = q
                .Include(o => o.Owner)
                .Include(o => o.Shop)
                .Include(o => o.Items)
                .Include(o => o.SupplierPurchases)
                .Include(o => o.IssueTags)
                    .ThenInclude(t => t.IssueTag)
                .Include(o => o.FulfilAccount)
                .Include(o => o.FulfilCard)
                .Include(o => o.PaidByUser);
        }

        // Keyword lookup ignores date range only (UI often still sends RangePicker).
        // Workflow tab / pendingFilter stay applied when the client sends them so queue
        // pages (USPS / Thank Card / pending / refund) can search within that queue.
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        if (!string.IsNullOrWhiteSpace(query.PlatformOrderStatus))
        {
            q = OrderWorkflowTabFilter.WhereWorkflowTab(q, query.PlatformOrderStatus);
        }

        if (!string.IsNullOrWhiteSpace(query.PendingFilter))
        {
            q = PendingQueueFilter.WherePendingFilter(q, query.PendingFilter);
        }

        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(o => o.OwnerId == query.OwnerUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Platform))
        {
            var platform = query.Platform.Trim().ToLowerInvariant();
            q = q.Where(o => o.Platform != null && o.Platform.ToLower() == platform);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(o => o.ShopId == query.ShopId.Value);
        }

        if (hasSearch)
        {
            var search = query.Search!.Trim();

            // A long run of digits is a code, not a person: order id, shipment, tracking, or phone.
            // Routing it to those columns skips six text columns (name, email, address, city, state,
            // postal) that such a keyword cannot meaningfully match, so the trigram indexes on the
            // code columns can carry the whole query.
            if (IsCodeShapedSearch(search))
            {
                q = q.Where(o =>
                    (o.PlatformOrderId != null && o.PlatformOrderId.Contains(search)) ||
                    (o.ShipmentNo != null && o.ShipmentNo.Contains(search)) ||
                    (o.TrackingNo != null && o.TrackingNo.Contains(search)) ||
                    o.SupplierPurchases.Any(p =>
                        (p.SupplierOrderNo != null && p.SupplierOrderNo.Contains(search))
                        || (p.TrackingNo != null && p.TrackingNo.Contains(search))) ||
                    (o.BuyerPhone != null && o.BuyerPhone.Contains(search)) ||
                    o.FourSellerOrderId.ToString().Contains(search));
            }
            else
            {
                q = q.Where(o =>
                    (o.PlatformOrderId != null && o.PlatformOrderId.Contains(search)) ||
                    (o.ShipmentNo != null && o.ShipmentNo.Contains(search)) ||
                    (o.TrackingNo != null && o.TrackingNo.Contains(search)) ||
                    o.SupplierPurchases.Any(p =>
                        (p.SupplierOrderNo != null && p.SupplierOrderNo.Contains(search))
                        || (p.TrackingNo != null && p.TrackingNo.Contains(search))) ||
                    (o.BuyerName != null && o.BuyerName.Contains(search)) ||
                    (o.BuyerPhone != null && o.BuyerPhone.Contains(search)) ||
                    (o.BuyerEmail != null && o.BuyerEmail.Contains(search)) ||
                    (o.Address != null && o.Address.Contains(search)) ||
                    (o.City != null && o.City.Contains(search)) ||
                    (o.State != null && o.State.Contains(search)) ||
                    (o.PostalCode != null && o.PostalCode.Contains(search)) ||
                    o.FourSellerOrderId.ToString().Contains(search));
            }
        }

        if (!hasSearch && query.DateFrom.HasValue)
        {
            var from = query.DateFrom.Value;
            q = q.Where(o => (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) >= from);
        }

        if (!hasSearch && query.DateTo.HasValue)
        {
            var to = query.DateTo.Value;
            q = q.Where(o => (o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt) <= to);
        }

        if (query.PurchasedToday == true)
        {
            var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AppTimeZones.Vietnam);
            var (dayFrom, dayTo) = AppTimeZones.GetVietnamDayUtcRange(DateOnly.FromDateTime(vnNow));
            q = q.Where(o =>
                o.FulfilPurchasedAt != null
                && o.FulfilPurchasedAt >= dayFrom
                && o.FulfilPurchasedAt <= dayTo);
        }
        else if (query.NeedsPurchase == true)
        {
            q = q.Where(o => o.FulfilPurchasedAt == null);
        }

        if (query.PaidByUserId.HasValue)
        {
            var paidBy = query.PaidByUserId.Value;
            q = q.Where(o => o.PaidByUserId == paidBy);
        }

        if (query.IsCustomerPaid.HasValue)
        {
            var paid = query.IsCustomerPaid.Value;
            q = q.Where(o => o.IsCustomerPaid == paid);
        }

        if (query.ProductSourceId.HasValue)
        {
            var domainsRaw = _db.ProductSources
                .AsNoTracking()
                .Where(p => p.Id == query.ProductSourceId.Value)
                .Select(p => p.Domains)
                .FirstOrDefault();

            var needles = ParseProductSourceDomainNeedles(domainsRaw);
            if (needles.Count == 0)
            {
                q = q.Where(_ => false);
            }
            else
            {
                // Local list → EF expands to OR of ILIKE/Contains; enough for SHEIN/Temu host patterns.
                q = q.Where(o => o.Items.Any(i =>
                    i.SourceUrl != null
                    && needles.Any(n => i.SourceUrl.ToLower().Contains(n))));
            }
        }

        return q;
    }

    /// <summary>
    /// Splits ProductSource.Domains into lowercase host needles for SourceUrl Contains matching.
    /// </summary>
    private static List<string> ParseProductSourceDomainNeedles(string? domains)
    {
        if (string.IsNullOrWhiteSpace(domains))
        {
            return [];
        }

        return domains
            .Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant()
                .Replace("https://", "", StringComparison.Ordinal)
                .Replace("http://", "", StringComparison.Ordinal))
            .Select(d =>
            {
                var slash = d.IndexOf('/');
                return slash >= 0 ? d[..slash] : d;
            })
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.IssueTags)
                .ThenInclude(t => t.IssueTag)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        return MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<OrderHoverPreviewDto> GetPreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // High enough to show every line item of a real order; the cap only guards against a
        // pathological order blowing up a hover payload.
        const int itemCap = 50;

        var header = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.Id,
                o.PlatformOrderId,
                o.PlatformOrderStatus,
                o.Currency,
                o.ShippingMethod,
                o.ShippingService,
                o.TrackingNo,
                o.PricingDetailSyncedAt,
                o.SellerNetAmount,
                o.TotalPaidAmount,
                o.Amount,
                o.ProductAmountBeforeDiscount,
                o.ProductPlatformDiscount,
                o.ProductSellerDiscount,
                o.ShippingFeeBeforeDiscount,
                o.ShippingFeeSellerDiscount,
                o.PlatformTotalTax,
                o.PlatformOtherFee,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        // Tổng NCC là SUM của các lần mua được tính tiền — hiện ra từng billno để một đơn Joint
        // Shipment vẫn đối chiếu 1-1 được với trang đơn của NCC. Cột `IsCharged` đã chứa kết quả
        // của OrderSupplierCost.SelectCharged nên hai chỗ không thể lệch nhau nữa.
        var purchaseRows = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.OrderId == id)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new OrderHoverPreviewSupplierLinkDto
            {
                SupplierOrderNo = p.SupplierOrderNo ?? string.Empty,
                SupplierOrderUrl = p.SupplierOrderUrl,
                RelationBillno = p.RelationBillno,
                ConfluenceBatchNo = p.ConfluenceBatchNo,
                SupplierStatus = p.SupplierStatus,
                TrackingNo = p.TrackingNo,
                Carrier = p.Carrier,
                TrackingKind = p.TrackingKind,
                WarehouseTrackingNo = p.WarehouseTrackingNo,
                WarehouseTrackingSeenAt = p.WarehouseTrackingSeenAt,
                SupplierUnpaid = p.SupplierUnpaid,
                IsCharged = p.IsCharged,
                SupplierRetailPrice = p.SupplierRetailPrice,
                SupplierSubtotal = p.SupplierSubtotal,
                SupplierShippingFee = p.SupplierShippingFee,
                SupplierRetailDeliveryFee = p.SupplierRetailDeliveryFee,
                SupplierOnTimeGuarantee = p.SupplierOnTimeGuarantee,
                SupplierCouponPrice = p.SupplierCouponPrice,
                SupplierMembershipDiscount = p.SupplierMembershipDiscount,
                SupplierPointsDiscount = p.SupplierPointsDiscount,
                SupplierWalletPrice = p.SupplierWalletPrice,
                SupplierTax = p.SupplierTax,
                SupplierTotal = p.SupplierTotal,
                CostDetailComplete = p.SupplierSubtotal != null && p.SupplierTotal != null,
            })
            .ToListAsync(cancellationToken);

        var costLinks = purchaseRows.Where(p => p.IsCharged).ToList();
        // Hover phải hiện lần mua đã khớp kể cả khi IsCharged=false (false-terminal).
        // Tổng tiền vẫn chỉ cộng dòng được tính — không lấy số của billno đã hoàn thật.
        var displayLinks = purchaseRows
            .Where(p => !string.IsNullOrWhiteSpace(p.SupplierOrderNo))
            .ToList();
        if (displayLinks.Count == 0)
        {
            displayLinks = purchaseRows;
        }

        decimal? SumCharged(Func<OrderHoverPreviewSupplierLinkDto, decimal?> selector)
        {
            decimal? total = null;
            foreach (var row in costLinks)
            {
                var value = selector(row);
                if (!value.HasValue) continue;
                total = (total ?? 0m) + value.Value;
            }
            return total;
        }

        var itemRows = await _db.OrderItems
            .AsNoTracking()
            .Where(i => i.OrderId == id)
            .OrderBy(i => i.FourSellerOrderItemId)
            .Select(i => new
            {
                i.Title,
                i.ImgUrl,
                i.Quantity,
                i.ItemPrice,
                i.SellerSku,
                i.VariantAttr,
            })
            .ToListAsync(cancellationToken);

        var sellAmount = header.SellerNetAmount
            ?? (header.TotalPaidAmount != 0 ? header.TotalPaidAmount : header.Amount);

        return new OrderHoverPreviewDto
        {
            Id = header.Id,
            PlatformOrderId = header.PlatformOrderId,
            PlatformOrderStatus = header.PlatformOrderStatus,
            Currency = string.IsNullOrWhiteSpace(header.Currency) ? "USD" : header.Currency,
            ShippingMethod = header.ShippingMethod,
            ShippingService = header.ShippingService,
            TrackingNo = header.TrackingNo,
            PricingDetailSyncedAt = header.PricingDetailSyncedAt,
            SellAmount = sellAmount,
            TotalPaidAmount = header.TotalPaidAmount,
            ProductAmountBeforeDiscount = header.ProductAmountBeforeDiscount,
            ProductPlatformDiscount = header.ProductPlatformDiscount,
            ProductSellerDiscount = header.ProductSellerDiscount,
            ShippingFeeBeforeDiscount = header.ShippingFeeBeforeDiscount,
            ShippingFeeSellerDiscount = header.ShippingFeeSellerDiscount,
            PlatformTotalTax = header.PlatformTotalTax,
            PlatformOtherFee = header.PlatformOtherFee,
            CostAmount = OrderSupplierCost.TrueSpend(
                SumCharged(p => p.SupplierTotal),
                SumCharged(p => p.SupplierWalletPrice),
                SumCharged(p => p.SupplierMembershipDiscount)),
            SupplierRetailPrice = SumCharged(p => p.SupplierRetailPrice),
            SupplierSubtotal = SumCharged(p => p.SupplierSubtotal),
            SupplierShippingFee = SumCharged(p => p.SupplierShippingFee),
            SupplierRetailDeliveryFee = SumCharged(p => p.SupplierRetailDeliveryFee),
            SupplierOnTimeGuarantee = SumCharged(p => p.SupplierOnTimeGuarantee),
            SupplierCouponPrice = SumCharged(p => p.SupplierCouponPrice),
            SupplierMembershipDiscount = SumCharged(p => p.SupplierMembershipDiscount),
            SupplierPointsDiscount = SumCharged(p => p.SupplierPointsDiscount),
            SupplierWalletPrice = SumCharged(p => p.SupplierWalletPrice),
            SupplierTax = SumCharged(p => p.SupplierTax),
            SupplierOrderCount = costLinks.Count > 0 ? costLinks.Count : displayLinks.Count,
            SupplierLinks = displayLinks,
            TotalQuantity = itemRows.Sum(i => i.Quantity),
            ItemCount = itemRows.Count,
            Items = itemRows
                .Take(itemCap)
                .Select(i => new OrderHoverPreviewItemDto
                {
                    Title = i.Title,
                    ImgUrl = i.ImgUrl,
                    Quantity = i.Quantity,
                    ItemPrice = i.ItemPrice,
                    SellerSku = i.SellerSku,
                    VariantAttr = i.VariantAttr,
                })
                .ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<OrderSyncResultDto> SyncAsync(
        OrderSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var acquired = await FourSellerSyncGate.TryEnterAsync(cancellationToken);
        if (!acquired)
        {
            _logger.LogInformation("Order sync skipped — another sync is already running");
            return new OrderSyncResultDto
            {
                FailedSellers =
                [
                    new OrderSyncSellerFailureDto
                    {
                        OwnerUserId = request.OwnerUserId ?? Guid.Empty,
                        OwnerName = null,
                        Message = "Đồng bộ đơn hàng đang chạy (thủ công hoặc tự động). Thử lại sau vài giây."
                    }
                ]
            };
        }

        try
        {
            return await SyncCoreAsync(request, cancellationToken);
        }
        finally
        {
            FourSellerSyncGate.Exit();
        }
    }

    private async Task<OrderSyncResultDto> SyncCoreAsync(
        OrderSyncRequest request,
        CancellationToken cancellationToken)
    {
        var statuses = (request.OrderStatuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s =>
            {
                var status = s.Trim();
                return string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase)
                    ? "canceled"
                    : status;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (statuses.Count == 0)
        {
            statuses = DefaultSyncStatuses.ToList();
        }

        var ownersQuery = _db.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie.Trim() != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn);

        if (request.OwnerUserId.HasValue)
        {
            ownersQuery = ownersQuery.Where(u => u.Id == request.OwnerUserId.Value);
        }

        var owners = await ownersQuery
            .Select(u => new { u.Id, u.UserName, u.FullName })
            .ToListAsync(cancellationToken);

        var result = new OrderSyncResultDto();

        if (request.OwnerUserId.HasValue && owners.Count == 0)
        {
            var ownerMeta = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == request.OwnerUserId.Value)
                .Select(u => new
                {
                    u.Id,
                    HasCookie = u.FourSellerCookie != null && u.FourSellerCookie.Trim() != string.Empty,
                    u.FourSellerCookieLoginStatus
                })
                .FirstOrDefaultAsync(cancellationToken);

            var message = ownerMeta is null
                ? "Seller không tồn tại hoặc chưa có cookie 4Seller."
                : !ownerMeta.HasCookie
                    ? "Seller không tồn tại hoặc chưa có cookie 4Seller."
                    : ownerMeta.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.Expired
                        ? "Cookie 4Seller đã hết hạn. Chạy Check login hoặc cập nhật cookie."
                        : "Cookie 4Seller chưa được xác nhận đăng nhập. Chạy Check login trước khi đồng bộ.";

            result.FailedSellers.Add(new OrderSyncSellerFailureDto
            {
                OwnerUserId = request.OwnerUserId.Value,
                Message = message
            });
            return result;
        }

        if (owners.Count == 0)
        {
            _logger.LogWarning(
                "Order sync found 0 users with FourSellerCookieLoginStatus=LoggedIn (ownerFilter={OwnerFilter})",
                request.OwnerUserId);
            return result;
        }

        // Fan-out by batch: within a batch each seller gets its own DI scope
        // (DbContext + FourSellerApiService + sticky proxy). Batches run sequentially so a large
        // roster cannot open unbounded parallel connections into the 4Seller proxy pool.
        var batchSize = request.MaxParallelOwners is > 0
            ? request.MaxParallelOwners.Value
            : owners.Count;

        foreach (var batch in OrderSyncScheduleConfig.ChunkOwners(owners, batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tasks = batch
                .Select(owner => SyncOwnerInScopeAsync(
                    owner.Id,
                    string.IsNullOrWhiteSpace(owner.FullName) ? owner.UserName : owner.FullName,
                    statuses,
                    request.DateFrom,
                    request.DateTo,
                    request.OpenStatusesIgnoreDateFilter,
                    request.SkipStalePricingSweep,
                    request.SkipNewOrderTelegram,
                    cancellationToken))
                .ToArray();

            var partials = await Task.WhenAll(tasks);
            foreach (var partial in partials)
            {
                if (partial.Failure is not null)
                {
                    result.FailedSellers.Add(partial.Failure);
                    continue;
                }

                result.SyncedSellers++;
                result.Inserted += partial.Inserted;
                result.Updated += partial.Updated;
            }
        }

        return result;
    }

    private async Task<OwnerSyncPartial> SyncOwnerInScopeAsync(
        Guid ownerId,
        string? ownerName,
        IReadOnlyList<string> statuses,
        DateTime? dateFrom,
        DateTime? dateTo,
        bool openStatusesIgnoreDateFilter,
        bool skipStalePricingSweep,
        bool skipNewOrderTelegram,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<IOrderSyncService>();
            var (inserted, updated) = await worker.SyncOwnerAsync(
                ownerId,
                statuses,
                dateFrom,
                dateTo,
                cancellationToken,
                openStatusesIgnoreDateFilter,
                skipStalePricingSweep,
                skipNewOrderTelegram);
            return new OwnerSyncPartial(inserted, updated, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Order sync failed for seller {OwnerId}", ownerId);
            return new OwnerSyncPartial(
                0,
                0,
                new OrderSyncSellerFailureDto
                {
                    OwnerUserId = ownerId,
                    OwnerName = ownerName,
                    Message = ex is BusinessException be ? be.Message : Truncate(ex.Message, 300)
                });
        }
    }

    private sealed record OwnerSyncPartial(
        int Inserted,
        int Updated,
        OrderSyncSellerFailureDto? Failure);

    /// <inheritdoc />
    public Task<(int Inserted, int Updated)> SyncOwnerAsync(
        Guid ownerUserId,
        IReadOnlyList<string> orderStatuses,
        DateTime? dateFrom,
        DateTime? dateTo,
        CancellationToken cancellationToken = default,
        bool openStatusesIgnoreDateFilter = false,
        bool skipStalePricingSweep = false,
        bool skipNewOrderTelegram = false)
    {
        var statuses = (orderStatuses ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s =>
            {
                var status = s.Trim();
                return string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase)
                    ? "canceled"
                    : status;
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (statuses.Count == 0)
        {
            statuses = DefaultSyncStatuses.ToList();
        }

        return SyncSellerAsync(
            ownerUserId,
            statuses,
            dateFrom,
            dateTo,
            openStatusesIgnoreDateFilter,
            skipStalePricingSweep,
            skipNewOrderTelegram,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FourSellerShippingServiceDto>> ListShippingServicesAsync(
        Guid ownerUserId,
        string? platform,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var ownerMeta = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => new
            {
                HasCookie = u.FourSellerCookie != null && u.FourSellerCookie.Trim() != string.Empty,
                u.FourSellerCookieLoginStatus
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (ownerMeta is null || !ownerMeta.HasCookie)
        {
            throw new BusinessException("Seller không tồn tại hoặc chưa có cookie 4Seller.");
        }

        if (ownerMeta.FourSellerCookieLoginStatus != FourSellerCookieLoginStatuses.LoggedIn)
        {
            throw new BusinessException(
                ownerMeta.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.Expired
                    ? "Cookie 4Seller đã hết hạn. Chạy Check login hoặc cập nhật cookie."
                    : "Cookie 4Seller chưa được xác nhận đăng nhập. Chạy Check login trước.");
        }

        if (refresh || await IsShippingServicesCacheStaleAsync(cancellationToken))
        {
            await RefreshShippingServicesCacheAsync(ownerUserId, cancellationToken);
        }

        var query = _db.PlatformShippingServices.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(platform))
        {
            var platformKey = platform.Trim();
            query = query.Where(s =>
                s.Platform != null &&
                s.Platform.ToLower() == platformKey.ToLower());
        }

        var fromDb = await query
            .Select(s => new FourSellerShippingServiceDto
            {
                Id = s.FourSellerId,
                Carrier = s.Carrier,
                Platform = s.Platform,
                Region = s.Region
            })
            .ToListAsync(cancellationToken);

        return DeduplicateCarriers(fromDb);
    }

    /// <inheritdoc />
    public async Task<MarkShipResultDto> MarkShipAsync(
        Guid orderId,
        MarkShipRequest request,
        CancellationToken cancellationToken = default)
    {
        var shippingService = request.ShippingService?.Trim() ?? string.Empty;
        var trackingNo = request.TrackingNo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shippingService))
        {
            throw new BusinessException("Shipping Service là bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(trackingNo))
        {
            throw new BusinessException("Tracking No là bắt buộc.");
        }

        var order = await _db.Orders
            .Include(o => o.Shop)
            .Include(o => o.SupplierPurchases)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        if (TikTokSellerNetCalculator.IsTikTokPlatformShipping(order.ShippingMethod))
        {
            throw new BusinessException(
                "Đơn TikTok Shipping — không Mark Ship / Đẩy track được. Tracking do TikTok xử lý.");
        }

        var already = await TryReconcileBeforeManualMarkShipAsync(order, trackingNo, cancellationToken);
        if (already is not null) return already;

        // Attach carrier link so requestId is persisted before poll (crash / unconfirmed recoverable).
        var autoLink = ResolveMarkShipErrorLink(order, trackingNo);

        // Prior unconfirmed: finish/reconcile first; if platform confirmed empty → clear + POST
        // in this same request (Manual never waits on Auto pool). Only block when remote unread.
        if (autoLink is not null && IsPendingManualMarkShipRetry(autoLink))
        {
            var (reconciled, outcome) = await TryFinishPendingManualMarkShipAsync(
                order, autoLink, trackingNo, shippingService, cancellationToken);
            if (reconciled is not null)
            {
                return reconciled;
            }

            var allowRepublish = OrderAutoMarkShipPolicy.AllowManualClearAndRepublish(
                confirmedAbsent: outcome == MarkShipReconcileOutcome.ConfirmedAbsent,
                safeToRetry: outcome == MarkShipReconcileOutcome.SafeToRetry);

            if (allowRepublish)
            {
                OrderAutoMarkShipPolicy.DowngradeUnconfirmedToFailed(
                    autoLink,
                    TruncateAutoMarkShipError(
                        "Mark Ship trước đó chưa xác nhận — Đẩy track thủ công gửi lại lên 4Seller."));
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (outcome == MarkShipReconcileOutcome.DifferentTracking)
            {
                throw new BusinessException(
                    autoLink.AutoMarkShipError
                    ?? "Sàn đã có tracking khác — không đẩy lại.");
            }
            else
            {
                throw new MarkShipUnconfirmedException(
                    "Không đọc được đơn trên 4Seller để xác nhận tracking — thử lại sau hoặc Sync đơn.",
                    autoLink.AutoMarkShipRequestId);
            }
        }

        try
        {
            return await ExecuteMarkShipAsync(
                order,
                shippingService,
                trackingNo,
                OrderFulfilTimeline.Source.Manual,
                DateTime.UtcNow,
                cancellationToken,
                pollMaxAttempts: ManualMarkShipPollAttempts,
                pollDelayMs: ManualMarkShipPollDelayMs,
                autoLink: autoLink);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MarkShipUnconfirmedException ex)
        {
            // Persist requestId on the matching carrier link so the next sync/manual can reconcile.
            await PersistManualMarkShipErrorAsync(
                order,
                trackingNo,
                AutoMarkShipStatuses.Unconfirmed,
                ex.Message,
                ex.RequestId,
                clearRequestId: false,
                cancellationToken);
            throw;
        }
        catch (BusinessException ex)
        {
            // Do not clear AutoMarkShipRequestId: a pre-submit failure must not wipe an older
            // unconfirmed request; a poll reject still keeps requestId for reconcile.
            await PersistManualMarkShipErrorAsync(
                order,
                trackingNo,
                AutoMarkShipStatuses.Failed,
                ex.Message,
                requestId: null,
                clearRequestId: false,
                cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            var link = ResolveMarkShipErrorLink(order, trackingNo);
            var keepUnconfirmed = link is not null
                && !string.IsNullOrWhiteSpace(link.AutoMarkShipRequestId);
            await PersistManualMarkShipErrorAsync(
                order,
                trackingNo,
                keepUnconfirmed ? AutoMarkShipStatuses.Unconfirmed : AutoMarkShipStatuses.Failed,
                ex.Message,
                requestId: null,
                clearRequestId: !keepUnconfirmed,
                cancellationToken);
            throw;
        }
    }

    private static SupplierPurchase? ResolveMarkShipErrorLink(Order order, string trackingNo)
    {
        return order.SupplierPurchases?
                .FirstOrDefault(l =>
                    l.TrackingKind == SupplierTrackingKinds.Carrier
                    && string.Equals(
                        (l.TrackingNo ?? l.AutoMarkShipTrackingNo)?.Trim(),
                        trackingNo,
                        StringComparison.OrdinalIgnoreCase))
            ?? order.SupplierPurchases?
                .FirstOrDefault(l => l.TrackingKind == SupplierTrackingKinds.Carrier);
    }

    private async Task PersistManualMarkShipErrorAsync(
        Order order,
        string trackingNo,
        string status,
        string? error,
        string? requestId,
        bool clearRequestId,
        CancellationToken cancellationToken)
    {
        var link = ResolveMarkShipErrorLink(order, trackingNo);
        if (link is null) return;

        link.AutoMarkShipTrackingNo = trackingNo;
        link.AutoMarkShipStatus = status;
        link.AutoMarkShipAttemptedAt = DateTime.UtcNow;
        link.AutoMarkShipError = TruncateAutoMarkShipError(error);
        if (clearRequestId)
        {
            link.AutoMarkShipRequestId = null;
        }
        else if (!string.IsNullOrWhiteSpace(requestId))
        {
            link.AutoMarkShipRequestId = requestId;
        }

        link.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Shared 4Seller Mark Ship core. Manual UI uses the action clock; automatic SHEIN fulfil
    /// uses the supplier's real tracking/ship clock and keeps SHEIN provenance.
    /// </summary>
    private async Task<MarkShipResultDto> ExecuteMarkShipAsync(
        Order order,
        string shippingService,
        string trackingNo,
        OrderFulfilTimeline.Source timelineSource,
        DateTime? trackingAt,
        CancellationToken cancellationToken,
        int pollMaxAttempts = 20,
        int pollDelayMs = 1500,
        SupplierPurchase? autoLink = null)
    {
        if (order.FourSellerShopId is null or 0)
        {
            throw new BusinessException("Đơn hàng thiếu shopId 4Seller — đồng bộ lại đơn rồi thử Mark Shipped.");
        }

        var platform = (order.Platform ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(platform))
        {
            throw new BusinessException("Đơn hàng thiếu platform — không thể Mark Shipped.");
        }

        var shopName = order.Shop?.Name ?? string.Empty;
        var platformOrderId = order.PlatformOrderId ?? string.Empty;
        var cookieOwnerId = await OrderFourSellerOwner.ResolveAsync(
            order, _db.Shops, cancellationToken);

        var requestId = await _fourSeller.MarkShipAsync(
            cookieOwnerId,
            [
                new FourSellerMarkShipItemDto
                {
                    Id = order.FourSellerOrderId,
                    Platform = platform,
                    ShopId = order.FourSellerShopId.Value,
                    ShopName = shopName,
                    PlatformOrderId = platformOrderId,
                    PlatformOrderNo = platformOrderId,
                    ShippingService = shippingService,
                    TrackingNo = trackingNo,
                    TrackingUrl = string.Empty
                }
            ],
            cancellationToken);

        if (autoLink is not null)
        {
            autoLink.AutoMarkShipRequestId = requestId;
            autoLink.AutoMarkShipTrackingNo = trackingNo;
            autoLink.AutoMarkShipAttemptedAt = DateTime.UtcNow;
            // Persist as unconfirmed before poll so a crash mid-poll can still reconcile
            // (never leave a dangling Succeeded without Order.TrackingNo).
            autoLink.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;
            autoLink.AutoMarkShipError = TruncateAutoMarkShipError(
                "Đã gửi Mark Shipped — đang chờ kết quả từ 4Seller.");
            await _db.SaveChangesAsync(cancellationToken);
        }

        var pollResult = await PollMarkShipResultAsync(
            cookieOwnerId, requestId, cancellationToken, pollMaxAttempts, pollDelayMs);
        if (!IsMarkShipSuccess(pollResult))
        {
            // Pending / timeout / ambiguous → Unconfirmed (keep requestId). Only explicit Failed
            // with a real reject message is safe to treat as BusinessException + clear requestId.
            if (!OrderAutoMarkShipPolicy.IsExplicitMarkShipReject(pollResult.Status, pollResult.Message))
            {
                var rescued = await TryRescueMarkShipFromRemoteAsync(
                    order,
                    trackingNo,
                    shippingService,
                    timelineSource,
                    trackingAt,
                    autoLink,
                    requestId,
                    cancellationToken);
                if (rescued is not null)
                {
                    return rescued;
                }

                // Không phải "thất bại": 4Seller đã nhận request, chỉ là chưa trả kết quả kịp vòng
                // poll. Người gọi phải reconcile trên sàn chứ không được tự đẩy lại cùng mã.
                throw new MarkShipUnconfirmedException(
                    "Đã gửi Mark Shipped lên 4Seller nhưng hết thời gian chờ kết quả —"
                    + " kiểm tra trạng thái trên sàn trước khi đẩy lại.",
                    requestId);
            }

            throw new BusinessException(
                string.IsNullOrWhiteSpace(pollResult.Message)
                    ? "4Seller từ chối Mark Shipped."
                    : pollResult.Message);
        }

        ApplyLocalMarkShipPublication(
            order, shippingService, trackingNo, timelineSource, trackingAt, autoLink);
        await _db.SaveChangesAsync(cancellationToken);
        await TryNotifyFulfilHandlingAsync([order], cancellationToken);

        return new MarkShipResultDto
        {
            Success = true,
            Message = pollResult.Message ?? "Mark Shipped thành công.",
            TrackingNo = trackingNo,
            RequestId = requestId,
            PlatformOrderStatus = order.PlatformOrderStatus
        };
    }

    private async Task ProcessAutoMarkShipCandidatesAsync(
        IReadOnlyList<AutoMarkShipCandidate> candidates,
        SheinOrderSyncResultDto result,
        int? maxAttempts,
        bool interactivePoll,
        CancellationToken cancellationToken)
    {
        var serviceCache =
            new Dictionary<(Guid OwnerId, string Platform), IReadOnlyList<FourSellerShippingServiceDto>>();
        var startedAt = DateTime.UtcNow;
        var attemptedRemote = 0;
        var deferred = 0;
        var attemptBudget = OrderAutoMarkShipPolicy.ResolveAttemptBudget(maxAttempts);
        var durationBudget = OrderAutoMarkShipPolicy.ResolveDurationBudget(maxAttempts);
        var (pollAttempts, pollDelayMs) = OrderAutoMarkShipPolicy.ResolveMarkShipPoll(interactivePoll);

        foreach (var candidate in candidates
                     .GroupBy(c => c.Link.Id)
                     .Select(g => g.Last()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var order = candidate.Order;
            var link = candidate.Link;

            // Legacy missing-carrier Block → pending (wait for track_info.name / retry when present).
            OrderAutoMarkShipPolicy.TryReopenMissingCarrierWait(link);

            // Race: ShippingMethod flipped to TikTok after queue select / ShouldAttempt miss.
            if (TikTokSellerNetCalculator.IsTikTokPlatformShipping(order.ShippingMethod))
            {
                MarkAutoShipBlocked(link, "Đơn TikTok Shipping — không Mark Ship.");
                result.AutoShipBlocked++;
                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: đơn TikTok Shipping — bỏ Auto Mark Ship.");
                await _db.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (!OrderAutoMarkShipPolicy.ShouldAttempt(order, link, DateTime.UtcNow)) continue;

            // Keep shein-sync under Cloudflare ~100s: defer excess / late candidates to next sync.
            if (attemptedRemote >= attemptBudget
                || DateTime.UtcNow - startedAt >= durationBudget)
            {
                deferred++;
                // Xin thêm một lượt POST ngay trong run này. Để dành cho lượt sync sau là mất hẳn:
                // mã vận đơn đã đứng yên nên fingerprint list không đổi và Auto Sync bỏ qua cả lượt.
                if (!string.IsNullOrWhiteSpace(candidate.Billno))
                {
                    result.NeedsDetailBillnos.Add(candidate.Billno);
                    result.NeedsDetailReasons.TryAdd(
                        candidate.Billno, SheinNeedsDetailReasons.PendingAutoMarkShip);
                }
                continue;
            }

            var trackingNo = link.TrackingNo?.Trim() ?? string.Empty;
            var supplierCarrier = link.Carrier?.Trim() ?? string.Empty;

            // SHEIN often omits track_info.name while shipping_no / method_real already identify the carrier.
            if (supplierCarrier.Length == 0)
            {
                var inferred = SheinCarrierResolver.Infer(null, null, trackingNo);
                if (!string.IsNullOrWhiteSpace(inferred))
                {
                    supplierCarrier = inferred;
                    link.Carrier = inferred;
                }
            }

            // shipping_no often appears while track_info.name is still null — wait, do not Block.
            if (supplierCarrier.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(candidate.Billno))
                {
                    result.NeedsDetailBillnos.Add(candidate.Billno);
                    result.NeedsDetailReasons.TryAdd(
                        candidate.Billno, SheinNeedsDetailReasons.PendingAutoMarkShip);
                }

                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: có tracking {trackingNo} nhưng chưa nhận diện được carrier — đợi sync sau.");
                continue;
            }

            link.AutoMarkShipTrackingNo = trackingNo;
            link.AutoMarkShipAttemptedAt = DateTime.UtcNow;

            var platform = (order.Platform ?? string.Empty).Trim();
            if (platform.Length == 0 || order.FourSellerShopId is null or 0)
            {
                MarkAutoShipBlocked(
                    link,
                    platform.Length == 0
                        ? "Đơn thiếu platform; không thể tự Mark Shipped."
                        : "Đơn thiếu shopId 4Seller; không thể tự Mark Shipped.");
                result.AutoShipBlocked++;
                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: {link.AutoMarkShipError} Cần xử lý thủ công.");
                await _db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var cookieOwnerId = await OrderFourSellerOwner.ResolveAsync(
                order, _db.Shops, cancellationToken);
            var cacheKey = (cookieOwnerId, platform.ToLowerInvariant());

            try
            {
                if (!serviceCache.TryGetValue(cacheKey, out var services))
                {
                    services = await ListShippingServicesAsync(
                        cookieOwnerId, platform, refresh: false, cancellationToken);
                    serviceCache[cacheKey] = services;
                }

                var matchedService = services
                    .Where(s => OrderAutoMarkShipPolicy.IsExactCarrierMatch(
                        supplierCarrier, s.Carrier))
                    .OrderByDescending(s =>
                        string.Equals(s.Carrier?.Trim(), supplierCarrier, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                // Prefer catalog display string (e.g. GOFO → GOFO. on TikTok).
                var catalogMatch = SheinCarrierResolver.MatchCatalog(
                    supplierCarrier,
                    services.Select(s => s.Carrier));
                if (!string.IsNullOrWhiteSpace(catalogMatch))
                {
                    matchedService = services.FirstOrDefault(s =>
                        string.Equals(s.Carrier?.Trim(), catalogMatch, StringComparison.OrdinalIgnoreCase))
                        ?? matchedService;
                }

                if (matchedService is null || string.IsNullOrWhiteSpace(matchedService.Carrier))
                {
                    MarkAutoShipBlocked(
                        link,
                        $"Không tìm thấy Shipping Service 4Seller khớp chính xác carrier '{supplierCarrier}'.");
                    result.AutoShipBlocked++;
                    result.Notes.Add(
                        $"SHEIN {candidate.Billno}: carrier '{supplierCarrier}' không khớp Shipping Service 4Seller"
                        + $" — tracking {trackingNo} cần Mark Shipped thủ công.");
                    await _db.SaveChangesAsync(cancellationToken);
                    continue;
                }

                attemptedRemote++;
                await ExecuteMarkShipAsync(
                    order,
                    matchedService.Carrier.Trim(),
                    trackingNo,
                    OrderFulfilTimeline.Source.Shein,
                    candidate.TrackingAt,
                    cancellationToken,
                    pollMaxAttempts: pollAttempts,
                    pollDelayMs: pollDelayMs,
                    autoLink: link);

                result.AutoShipped++;
                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: Auto Mark Shipped thành công"
                    + $" · {matchedService.Carrier.Trim()} · {trackingNo}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MarkShipUnconfirmedException ex)
            {
                // Request đã tới 4Seller. Unconfirmed (không Failed): Failed sẽ đẩy lại sau 15 phút.
                link.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;
                link.AutoMarkShipError = TruncateAutoMarkShipError(ex.Message);
                link.AutoMarkShipRequestId = ex.RequestId ?? link.AutoMarkShipRequestId;
                link.AutoMarkShipAttemptedAt = DateTime.UtcNow;
                result.AutoShipBlocked++;
                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: đã gửi Mark Shipped ({trackingNo}) nhưng chưa nhận được"
                    + " kết quả — sẽ reconcile trên sàn trước khi thử lại.");
                _logger.LogWarning(
                    ex,
                    "Auto Mark Ship unconfirmed for SHEIN {Billno}, order {OrderId}, request {RequestId}",
                    candidate.Billno,
                    order.Id,
                    ex.RequestId);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (BusinessException ex)
            {
                // Explicit 4Seller / validation reject — safe to Failed + cooldown retry.
                link.AutoMarkShipStatus = AutoMarkShipStatuses.Failed;
                link.AutoMarkShipError = TruncateAutoMarkShipError(ex.Message);
                link.AutoMarkShipRequestId = null;
                link.AutoMarkShipAttemptedAt = DateTime.UtcNow;
                result.AutoShipFailed++;
                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: Auto Mark Shipped lỗi — {link.AutoMarkShipError}"
                    + " (sẽ thử lại sau).");
                _logger.LogWarning(
                    ex,
                    "Auto Mark Ship rejected for SHEIN {Billno}, order {OrderId}, tracking {TrackingNo}",
                    candidate.Billno,
                    order.Id,
                    trackingNo);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Network/crash after submit: keep Unconfirmed + requestId (never blind republish).
                if (!string.IsNullOrWhiteSpace(link.AutoMarkShipRequestId))
                {
                    link.AutoMarkShipStatus = AutoMarkShipStatuses.Unconfirmed;
                    link.AutoMarkShipError = TruncateAutoMarkShipError(ex.Message);
                    link.AutoMarkShipAttemptedAt = DateTime.UtcNow;
                    result.AutoShipBlocked++;
                    result.Notes.Add(
                        $"SHEIN {candidate.Billno}: Mark Ship đã gửi nhưng lỗi sau đó — {link.AutoMarkShipError}"
                        + " (giữ unconfirmed; sẽ reconcile, không đẩy lại mù).");
                }
                else
                {
                    link.AutoMarkShipStatus = AutoMarkShipStatuses.Failed;
                    link.AutoMarkShipError = TruncateAutoMarkShipError(ex.Message);
                    link.AutoMarkShipRequestId = null;
                    result.AutoShipFailed++;
                    result.Notes.Add(
                        $"SHEIN {candidate.Billno}: Auto Mark Shipped lỗi — {link.AutoMarkShipError}"
                        + " (sẽ thử lại sau).");
                }

                _logger.LogWarning(
                    ex,
                    "Auto Mark Ship failed for SHEIN {Billno}, order {OrderId}, tracking {TrackingNo}",
                    candidate.Billno,
                    order.Id,
                    trackingNo);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        if (deferred > 0)
        {
            result.Notes.Add(
                $"Auto Mark Ship: hoãn {deferred} đơn sang lần sync sau"
                + $" (giới hạn {OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest}/request,"
                + $" ngân sách {OrderAutoMarkShipPolicy.MaxDurationPerSyncRequest.TotalSeconds:0}s).");
        }
    }

    private static void MarkAutoShipBlocked(SupplierPurchase link, string error)
    {
        link.AutoMarkShipStatus = AutoMarkShipStatuses.Blocked;
        link.AutoMarkShipError = TruncateAutoMarkShipError(error);
        link.AutoMarkShipRequestId = null;
    }

    private static string TruncateAutoMarkShipError(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "Lỗi không xác định." : value.Trim();
        return text.Length <= 1000 ? text : text[..1000];
    }

    /// <summary>
    /// Sends merged USPS / warehouse Telegram notify and persists dedup stamps when any message was sent.
    /// </summary>
    private async Task TryNotifyFulfilHandlingAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        if (orders.Count == 0) return;

        try
        {
            var sent = await _telegram.NotifyFulfilHandlingAsync(orders, shopNameFallbacks: null, cancellationToken);
            if (sent > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Telegram fulfil-handling notify failed for {Count} orders",
                orders.Count);
        }
    }

    /// <summary>
    /// Sends the "Add track" Telegram message for orders this backend sweep just gave real carrier
    /// tracking to (or deferred from a prior pass). Called after Auto Mark Ship so the message
    /// carries its outcome. Persists pending/notified stamps when the notifier mutates orders.
    /// </summary>
    private async Task TryNotifySheinDetailAddTrackAsync(
        IReadOnlyList<SheinDetailAddTrackNotifyItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return;

        try
        {
            var sent = await _telegram.NotifySheinDetailAddTrackAsync(items, cancellationToken);
            if (_db.ChangeTracker.HasChanges())
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Telegram SHEIN add-track: watchlist={Watchlist} sent={Sent}",
                items.Count,
                sent);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Telegram SHEIN add-track notify failed for {Count} orders",
                items.Count);
        }
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateCostAsync(
        Guid orderId,
        UpdateOrderCostRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateCostField(request.SupplierRetailPrice, "SupplierRetailPrice");
        ValidateCostField(request.SupplierSubtotal, "SupplierSubtotal");
        ValidateCostField(request.SupplierShippingFee, "SupplierShippingFee");
        ValidateCostField(request.SupplierRetailDeliveryFee, "SupplierRetailDeliveryFee");
        ValidateCostField(request.SupplierOnTimeGuarantee, "SupplierOnTimeGuarantee");
        ValidateCostField(request.SupplierCouponPrice, "SupplierCouponPrice");
        ValidateCostField(request.SupplierMembershipDiscount, "SupplierMembershipDiscount");
        ValidateCostField(request.SupplierPointsDiscount, "SupplierPointsDiscount");
        ValidateCostField(request.SupplierWalletPrice, "SupplierWalletPrice");
        ValidateCostField(request.SupplierTax, "SupplierTax");
        ValidateCostField(request.SupplierTotal, "SupplierTotal");

        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            // Required: the ledger is reconciled per billno, and an unloaded collection would
            // look like "order has no supplier purchases" and wipe the per-billno rows.
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        Guid? accountId = request.FulfilAccountId;
        Guid? cardId = request.FulfilCardId;
        // Chỉ ghi người mua khi request gửi giá trị; không gửi thì giữ cũ, chỉ fallback
        // actingUserId khi đơn chưa có người mua (ghi nhận lần mua đầu tiên).
        Guid? paidByUserId = request.PaidByUserId;
        if (paidByUserId is null && order.PaidByUserId is null && actingUserId != Guid.Empty)
        {
            paidByUserId = actingUserId;
        }

        if (accountId.HasValue
            && !await _db.FulfilAccounts.AnyAsync(a => a.Id == accountId.Value, cancellationToken))
        {
            throw new NotFoundException("Không tìm thấy tài khoản fulfil.");
        }

        if (cardId.HasValue)
        {
            var card = await _db.FulfilCards.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == cardId.Value, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy thẻ thanh toán.");

            if (!card.IsActive)
            {
                throw new BusinessException("Thẻ đang vô hiệu.");
            }

            if (accountId.HasValue)
            {
                var linked = await _db.FulfilAccountCards.AnyAsync(
                    l => l.FulfilAccountId == accountId.Value && l.FulfilCardId == cardId.Value,
                    cancellationToken);
                if (!linked)
                {
                    throw new BusinessException("Thẻ chưa được gắn với tài khoản mua hàng đã chọn.");
                }
            }
        }

        if (paidByUserId.HasValue
            && !await _db.Users.AnyAsync(u => u.Id == paidByUserId.Value, cancellationToken))
        {
            throw new NotFoundException("Không tìm thấy người thanh toán.");
        }

        await UpsertManualPurchaseAsync(
            order, request, accountId, cardId, paidByUserId, cancellationToken);
        order.FulfilAccountId = accountId;
        order.FulfilCardId = cardId;
        // Không wipe PaidByUserId khi bỏ account/card — người mua ≠ khoản tiền.
        order.PaidByUserId = paidByUserId ?? order.PaidByUserId;
        if (request.FulfilPurchasedAt.HasValue)
        {
            // Explicit human-entered purchase time overrides any prior sync guess (Manual rank).
            StampManualMilestone(
                order, OrderFulfilTimeline.Milestone.FulfilPurchased, request.FulfilPurchasedAt);
        }
        else if ((accountId.HasValue || cardId.HasValue) && order.FulfilPurchasedAt is null)
        {
            // Recording a purchase now is itself the human-action clock.
            OrderFulfilTimeline.Stamp(
                order, OrderFulfilTimeline.Milestone.FulfilPurchased,
                DateTime.UtcNow, OrderFulfilTimeline.Source.Manual);
        }
        OrderFulfilTimeline.Normalize(order);
        order.UpdatedAt = DateTime.UtcNow;

        await ReconcileCostUpdateTransactionsAsync(
            order,
            actingUserId,
            paidByOverride: order.PaidByUserId,
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        var refreshed = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstAsync(o => o.Id == orderId, cancellationToken);

        // Preserve source URL enrichments already applied on tracked order items.
        refreshed.Items = order.Items;
        return MapDetail(refreshed);
    }

    /// <summary>
    /// Ghi số nhập tay của nút "Cập nhật phí" thành một dòng <c>SupplierPurchases</c> nguồn
    /// <c>manual</c> — chỗ duy nhất còn giữ thông tin mua hàng sau khi mười một cột
    /// <c>Orders.Supplier*</c> bị bỏ.
    ///
    /// Chỉ ghi khi số gửi lên <b>khác</b> tổng đang tính từ các dòng NCC. Modal điền sẵn giá trị
    /// hiện có, nên bấm Lưu chỉ để đổi tài khoản/thẻ sẽ gửi lại đúng con số của SHEIN; biến nó
    /// thành một dòng nhập tay là đóng băng giá mua ở giá trị hôm nay và mọi lượt sync sau không
    /// sửa được nữa (dòng nhập tay có tiền thắng theo <see cref="OrderSupplierCost.SelectCharged"/>).
    /// </summary>
    private async Task UpsertManualPurchaseAsync(
        Order order,
        UpdateOrderCostRequest request,
        Guid? accountId,
        Guid? cardId,
        Guid? paidByUserId,
        CancellationToken cancellationToken)
    {
        order.SupplierPurchases ??= new List<SupplierPurchase>();
        var now = DateTime.UtcNow;

        var manual = order.SupplierPurchases
            .FirstOrDefault(p => p.Source == SupplierPurchaseSources.Manual);

        var billno = string.IsNullOrWhiteSpace(request.SupplierOrderNo)
            ? null
            : request.SupplierOrderNo.Trim();

        var supplierSide = order.SupplierPurchases
            .Where(p => p.Source != SupplierPurchaseSources.Manual)
            .ToList();
        var charged = OrderSupplierCost.SelectCharged(supplierSide);

        decimal? Synced(Func<SupplierPurchase, decimal?> selector)
        {
            decimal? total = null;
            foreach (var purchase in charged)
            {
                var value = selector(purchase);
                if (!value.HasValue) continue;
                total = (total ?? 0m) + value.Value;
            }
            return total;
        }

        static bool Same(decimal? a, decimal? b) =>
            a is null && b is null || a.HasValue && b.HasValue && Math.Abs(a.Value - b.Value) < 0.005m;

        var matchesSynced =
            Same(request.SupplierTotal, Synced(p => p.SupplierTotal))
            && Same(request.SupplierRetailPrice, Synced(p => p.SupplierRetailPrice))
            && Same(request.SupplierSubtotal, Synced(p => p.SupplierSubtotal))
            && Same(request.SupplierShippingFee, Synced(p => p.SupplierShippingFee))
            && Same(request.SupplierRetailDeliveryFee, Synced(p => p.SupplierRetailDeliveryFee))
            && Same(request.SupplierOnTimeGuarantee, Synced(p => p.SupplierOnTimeGuarantee))
            && Same(request.SupplierCouponPrice, Synced(p => p.SupplierCouponPrice))
            && Same(request.SupplierMembershipDiscount, Synced(p => p.SupplierMembershipDiscount))
            && Same(request.SupplierPointsDiscount, Synced(p => p.SupplierPointsDiscount))
            && Same(request.SupplierWalletPrice, Synced(p => p.SupplierWalletPrice))
            && Same(request.SupplierTax, Synced(p => p.SupplierTax));

        // Số trùng với tổng SHEIN và không có mã nào để lưu thêm ⇒ không có gì thuộc về dòng nhập
        // tay. Dòng cũ (nếu có) được gỡ để sổ cái quay lại quy kết theo từng billno.
        if (matchesSynced && billno is null)
        {
            if (manual is not null)
            {
                order.SupplierPurchases.Remove(manual);
                _db.SupplierPurchases.Remove(manual);
            }

            OrderSupplierCost.ApplyAggregates(order);
            return;
        }

        if (manual is null)
        {
            manual = new SupplierPurchase
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Source = SupplierPurchaseSources.Manual,
                MatchState = SupplierPurchaseMatchStates.Matched,
                MatchedAt = now,
                MatchedByUserId = paidByUserId,
                FirstSeenAt = now,
                LastSeenAt = now,
                CreatedAt = now
            };
            order.SupplierPurchases.Add(manual);
            _db.SupplierPurchases.Add(manual);
        }

        // Mã NCC gõ tay chỉ được giữ khi chưa dòng nào khác sở hữu — unique index toàn DB, và một
        // billno đã thuộc dòng sync thì dòng sync mới là chủ của nó.
        if (billno is not null && !string.Equals(manual.SupplierOrderNo, billno, StringComparison.OrdinalIgnoreCase))
        {
            var taken = await _db.SupplierPurchases
                .AnyAsync(
                    p => p.Id != manual.Id && p.SupplierOrderNo != null && p.SupplierOrderNo.ToLower() == billno.ToLower(),
                    cancellationToken);
            manual.SupplierOrderNo = taken ? null : billno;
        }
        else if (billno is null)
        {
            manual.SupplierOrderNo = null;
        }

        manual.SupplierRetailPrice = matchesSynced ? null : request.SupplierRetailPrice;
        manual.SupplierSubtotal = matchesSynced ? null : request.SupplierSubtotal;
        manual.SupplierShippingFee = matchesSynced ? null : request.SupplierShippingFee;
        manual.SupplierRetailDeliveryFee = matchesSynced ? null : request.SupplierRetailDeliveryFee;
        manual.SupplierOnTimeGuarantee = matchesSynced ? null : request.SupplierOnTimeGuarantee;
        manual.SupplierCouponPrice = matchesSynced ? null : request.SupplierCouponPrice;
        manual.SupplierMembershipDiscount = matchesSynced ? null : request.SupplierMembershipDiscount;
        manual.SupplierPointsDiscount = matchesSynced ? null : request.SupplierPointsDiscount;
        manual.SupplierWalletPrice = matchesSynced ? null : request.SupplierWalletPrice;
        manual.SupplierTax = matchesSynced ? null : request.SupplierTax;
        manual.SupplierTotal = matchesSynced ? null : request.SupplierTotal;

        if (!string.IsNullOrWhiteSpace(request.SupplierOrderNo) && manual.SupplierOrderUrl is null)
        {
            manual.SupplierOrderUrl = $"https://us.shein.com/user/orders/detail/{request.SupplierOrderNo.Trim()}";
        }

        manual.FulfilAccountId = accountId;
        manual.FulfilCardId = cardId;
        // Giữ người mua cũ khi request không gửi; không wipe khi bỏ account/card.
        manual.PaidByUserId = paidByUserId ?? manual.PaidByUserId;
        manual.SupplierPaidAt ??= request.FulfilPurchasedAt;
        manual.LastSeenAt = now;
        manual.UpdatedAt = now;

        OrderSupplierCost.ApplyAggregates(order);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateMilestonesAsync(
        Guid orderId,
        UpdateOrderMilestonesRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default,
        Guid? sessionId = null)
    {
        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .Include(o => o.FulfilClaimedByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        // Explicit human milestone edits win over any sync stamp (Manual rank, forced overwrite).
        // Chỉ còn cho phép mốc mua hàng (+ Track hiếm khi cần). Transit/Giao/Refund chỉ từ sync
        // (SHEIN / 4Seller) — không stamp tay để tránh timeline lệch cột trạng thái sàn.
        if (request.InTransitAt.HasValue
            || request.DeliveredAt.HasValue
            || request.RefundedAt.HasValue)
        {
            throw new BusinessException(
                "Không hỗ trợ đánh dấu In Transit / Delivered / Refund thủ công. "
                + "Mốc Transit/Giao lấy từ Sync Fulfil (SHEIN); Hủy/Refund lấy từ Sync 4Seller.");
        }

        if (request.ClearFulfilPurchasedAt && request.FulfilPurchasedAt.HasValue)
        {
            throw new BusinessException(
                "Không thể vừa đặt vừa xóa fulfilPurchasedAt trong cùng request.");
        }

        if (request.ClearFulfilPurchasedAt)
        {
            var hasBillno = (order.SupplierPurchases ?? [])
                .Any(p => !string.IsNullOrWhiteSpace(p.SupplierOrderNo));
            if (hasBillno)
            {
                throw new BusinessException(
                    "Đơn đã gắn billno mua hàng — không thể chuyển về chưa mua. "
                    + "Gỡ link NCC trước nếu cần chỉnh tay.");
            }
        }

        var now = DateTime.UtcNow;

        // Cú bấm «Đã mua» vẫn nhận đơn khi sync đã stamp FulfilPurchasedAt trước — điều kiện cũ
        // (FulfilPurchasedAt is null) làm mất attribution + attempt Purchased. Chỉ bỏ qua khi đơn
        // đã có người mua VÀ đã có attempt Purchased (đã nhận rồi).
        var hasPurchasedAttempt = !request.ClearFulfilPurchasedAt
            && request.FulfilPurchasedAt.HasValue
            && await _db.OrderFulfilAttempts.AnyAsync(
                a => a.OrderId == orderId && a.Outcome == FulfilAttemptOutcomes.Purchased,
                cancellationToken);
        var claimsPurchase =
            !request.ClearFulfilPurchasedAt
            && request.FulfilPurchasedAt.HasValue
            && actingUserId != Guid.Empty
            && (order.PaidByUserId is null || !hasPurchasedAttempt);

        if (claimsPurchase)
        {
            var leaseActive = IsClaimActive(order, now);
            if (leaseActive
                && order.FulfilClaimedByUserId is { } holderId
                && holderId != actingUserId
                && !request.Force)
            {
                var holderName = string.IsNullOrWhiteSpace(order.FulfilClaimedByUser?.FullName)
                    ? order.FulfilClaimedByUser?.UserName
                    : order.FulfilClaimedByUser?.FullName;
                var remain = order.FulfilClaimExpiresAt is { } e
                    ? FormatRemain(e - now)
                    : "?";
                throw new ConflictException(
                    $"Đơn đang do {holderName ?? "người khác"} giữ (còn {remain}).");
            }

            var forceTakeover = leaseActive
                && order.FulfilClaimedByUserId is { } h
                && h != actingUserId
                && request.Force;

            await FinalizePurchaseAttemptsAsync(
                order,
                actingUserId,
                sessionId,
                request.FulfilPurchasedAt!.Value,
                now,
                forceTakeover,
                cancellationToken);

            // Người bấm «Đã mua» là nguồn sự thật — ghi đè cả khi sync đoán sai trước đó.
            order.PaidByUserId = actingUserId;

            // Lan xuống lần mua + sổ cái chỉ khi đã có purchase (bấm trước sync thì bỏ qua).
            EnsureSupplierPurchases(order);
            if (order.SupplierPurchases is { Count: > 0 })
            {
                foreach (var purchase in order.SupplierPurchases)
                {
                    purchase.PaidByUserId ??= order.PaidByUserId;
                }

                await ReconcileCostUpdateTransactionsAsync(
                    order,
                    actingUserId,
                    paidByOverride: order.PaidByUserId,
                    cancellationToken);
            }
        }

        if (request.ClearFulfilPurchasedAt)
        {
            OrderFulfilTimeline.Clear(order, OrderFulfilTimeline.Milestone.FulfilPurchased);
            SyncFulfilAttributionWithPurchases(order);
            // Gỡ stamp tay = gỡ người mua; canUnmarkPurchased chỉ cho phép khi chưa gắn billno.
            order.PaidByUserId = null;
            foreach (var purchase in order.SupplierPurchases ?? [])
            {
                if (!string.Equals(purchase.Source, SupplierPurchaseSources.Manual, StringComparison.OrdinalIgnoreCase))
                {
                    purchase.PaidByUserId = null;
                }
            }

            await RevertPurchaseAttemptAsync(orderId, now, cancellationToken);
        }
        else
        {
            StampManualMilestone(order, OrderFulfilTimeline.Milestone.FulfilPurchased, request.FulfilPurchasedAt);
        }

        StampManualMilestone(order, OrderFulfilTimeline.Milestone.TrackingAdded, request.TrackingAddedAt);
        OrderFulfilTimeline.Normalize(order);

        order.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        if (claimsPurchase)
        {
            await _fulfilQueue.PublishClaimChangedAsync(
                [
                    new FulfilClaimItemDto
                    {
                        OrderId = orderId,
                        State = FulfilClaimStates.Purchased,
                        SessionCount = 0,
                        SessionIds = [],
                    }
                ],
                now,
                cancellationToken);
        }

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        return MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<SheinOrderSyncResultDto> SyncFromSheinAsync(
        SheinOrderSyncRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var result = new SheinOrderSyncResultDto();
        var startedAt = DateTime.UtcNow;

        var sheinOrders = (request.Orders ?? [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Billno))
            .ToList();
        result.Fetched = sheinOrders.Count;
        if (sheinOrders.Count == 0)
        {
            return result;
        }

        // Không mutex in-process: lượt song song được phép; ai SaveChanges trước thì ghi trước.
        // Unique SupplierOrderNo + AutoMarkShipStatus xử lý race hiếm.
        return await SyncFromSheinCoreAsync(
            request,
            sheinOrders,
            actingUserId,
            result,
            startedAt,
            cancellationToken);
    }

    /// <summary>
    /// Body of <see cref="SyncFromSheinAsync"/> — ingest/match/SaveChanges rồi Auto Mark Ship.
    /// </summary>
    private async Task<SheinOrderSyncResultDto> SyncFromSheinCoreAsync(
        SheinOrderSyncRequest request,
        List<SheinOrderDto> sheinOrders,
        Guid actingUserId,
        SheinOrderSyncResultDto result,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        var phase = (request.Phase ?? "list").Trim().ToLowerInvariant();
        if (phase is not ("list" or "detail"))
        {
            phase = "list";
        }

        // Cùng khách + cùng SKU thì điểm khớp bằng nhau; xử lý billno mua sớm trước thì đơn đặt
        // sớm nhận billno sớm — kết quả tất định qua các lượt, không phụ thuộc thứ tự payload.
        SortSheinOrdersForMatch(sheinOrders);

        var syncConfig = await GetSheinSyncConfigAsync(cancellationToken);
        var modeRaw = (request.UpdateMode ?? syncConfig.UpdateMode ?? ExtensionSheinSyncSettingKeys.UpdateModeNew)
            .Trim()
            .ToLowerInvariant();
        // all = mở rộng pool khớp/cập nhật theo Lookback; không xóa supplier link hàng loạt.
        var isUpdateAll = modeRaw == ExtensionSheinSyncSettingKeys.UpdateModeAll;
        var lookbackDays = Math.Clamp(
            syncConfig.RefreshLookbackDays > 0
                ? syncConfig.RefreshLookbackDays
                : ExtensionSheinSyncSettingKeys.DefaultRefreshLookbackDays,
            ExtensionSheinSyncSettingKeys.MinRefreshLookbackDays,
            ExtensionSheinSyncSettingKeys.MaxRefreshLookbackDays);
        result.Notes.Add(
            isUpdateAll
                ? "Chế độ cập nhật: all — pool mọi đơn sàn từ mốc mua sớm nhất của lô lùi "
                  + $"≥{SheinMatchPoolMinLeadDays} ngày / Lookback (trần {SheinMatchPoolMaxAgeDays} ngày)."
                : "Chế độ cập nhật: new — pool fulfil đang chờ trong cửa sổ lead từ mốc mua "
                  + $"(≥{SheinMatchPoolMinLeadDays} ngày / Lookback) + đơn đã gắn billno của lô.");

        var billnosLower = sheinOrders
            .Select(o => o.Billno.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var fulfilStatuses = PlatformOrderStatuses.FulfilCandidateStatuses;
        var ordersNeedingCostRefresh = new HashSet<Guid>();

        // Cửa sổ pool suy từ mốc mua sớm nhất của lô: đơn sàn đặt trước khi mua SHEIN (lead time
        // fulfil tới hạn ship). Mode `new` dùng cùng lead với `all` — trước đây `UtcNow - 90` làm
        // biên dưới và load mọi fulfil trong 90 ngày kèm Include đầy đủ, trong khi Auto chỉ gửi ~20
        // billno. Chặn trên 90 ngày để pool không phình.
        var utcNow = DateTime.UtcNow;
        var earliestPurchase = sheinOrders
            .Select(o => o.SheinPaidAt ?? o.OrderedAt)
            .Where(t => t.HasValue)
            .Select(t => NormalizeUtc(t!.Value))
            .DefaultIfEmpty(utcNow.AddDays(-lookbackDays))
            .Min();
        var poolSince = ComputeSheinMatchPoolSince(earliestPurchase, lookbackDays, utcNow);

        // Hai bước: lấy id trước (không Include), rồi materialize kèm navigation chỉ cho tập đó —
        // tránh kéo hàng trăm fulfil không liên quan khi lô chỉ có vài chục billno.
        List<Guid> candidateIds;
        if (isUpdateAll)
        {
            // Pool rộng: mọi đơn trong cửa sổ (+ luôn gồm đơn đã gắn billno của lô này).
            // Nhận cả đơn đã huỷ/hoàn trên sàn: tiền đã trả SHEIN không mất vì khách huỷ TikTok.
            candidateIds = await _db.Orders
                .AsNoTracking()
                .Where(o =>
                    o.SupplierPurchases.Any(p =>
                        p.SupplierOrderNo != null && billnosLower.Contains(p.SupplierOrderNo.ToLower()))
                    || o.OrderCreateTime == null
                    || o.OrderCreateTime >= poolSince)
                .OrderBy(o => o.OrderCreateTime)
                .ThenBy(o => o.Id)
                .Select(o => o.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            // Mode new = hợp hai tập:
            // 1) đơn đã gắn billno trong lô (cập nhật status/tracking/cost incremental),
            // 2) fulfil đang mở trong cửa sổ lead (ứng viên khớp mới).
            candidateIds = await _db.Orders
                .AsNoTracking()
                .Where(o =>
                    o.SupplierPurchases.Any(p =>
                        p.SupplierOrderNo != null && billnosLower.Contains(p.SupplierOrderNo.ToLower()))
                    || ((o.OrderCreateTime == null || o.OrderCreateTime >= poolSince)
                        && ((o.PlatformOrderStatus != null && fulfilStatuses.Contains(o.PlatformOrderStatus))
                            || ((o.PlatformOrderStatus == null || o.PlatformOrderStatus == "")
                                && (o.OrderStatus == "to_ship"
                                    || o.OrderStatus == "in_process"
                                    || o.OrderStatus == "pending"
                                    || o.OrderStatus == "on_hold")))))
                .OrderBy(o => o.OrderCreateTime)
                .ThenBy(o => o.Id)
                .Select(o => o.Id)
                .ToListAsync(cancellationToken);
        }

        var candidates = candidateIds.Count == 0
            ? new List<Order>()
            : await _db.Orders
                .Include(o => o.Shop)
                .Include(o => o.Items)
                .Include(o => o.SupplierPurchases)
                .AsSplitQuery()
                .Where(o => candidateIds.Contains(o.Id))
                .OrderBy(o => o.OrderCreateTime)
                .ThenBy(o => o.Id)
                .ToListAsync(cancellationToken);

        var accountLookup = await BuildFulfilAccountEmailLookupAsync(cancellationToken);
        var pool = candidates.ToList();
        var now = DateTime.UtcNow;
        var claimedThisPass = new HashSet<Guid>();
        var purchaseMarks = await BuildPurchaseMarkIndexAsync(pool, sheinOrders, cancellationToken);
        var changed = false;
        var autoMarkShipCandidates = new List<AutoMarkShipCandidate>();
        var autoMarkShipReconcileCandidates = new List<AutoMarkShipCandidate>();

        var detailAddTrackWatchlist = new Dictionary<Guid, SheinDetailAddTrackNotifyItem>();

        // Bước 1 — ghi nhận. Mọi billno của lô vào bảng trước khi chạy khớp, nên một guard skip hay
        // pool hụt cũng không làm mất dấu vết lần mua nào.
        var purchasesByBillno = await SupplierPurchaseIngest.LoadByBillnoAsync(
            _db, billnosLower, cancellationToken);

        foreach (var shein in sheinOrders)
        {
            var billno = shein.Billno.Trim();
            var (orderAccountId, orderAccountName, accountNote) =
                ResolveFulfilAccountFromEmail(shein.AccountEmail, accountLookup);
            if (!orderAccountId.HasValue && request.SyncFulfilAccountId.HasValue)
            {
                orderAccountId = request.SyncFulfilAccountId;
                orderAccountName = accountLookup.Values
                    .Where(v => v.Id == orderAccountId.Value)
                    .Select(v => v.Name)
                    .FirstOrDefault();
                accountNote ??= orderAccountName is null
                    ? $"fallback session account {orderAccountId}."
                    : $"fallback session account {orderAccountName}.";
            }
            if (!string.IsNullOrWhiteSpace(accountNote))
            {
                result.Notes.Add($"SHEIN {billno}: {accountNote}");
            }

            // Chụp trạng thái **trước** khi ghi nhận: bước ghi nhận đã áp payload lên dòng mua, nên so
            // dòng mua với payload sau đó luôn ra "không đổi" và chế độ Chỉ đơn mới sẽ bỏ qua tất cả.
            var snapshot = SheinPurchaseSnapshot.Of(
                purchasesByBillno.TryGetValue(billno, out var existingPurchase) ? existingPurchase : null);

            var purchase = SupplierPurchaseIngest.Upsert(
                _db, purchasesByBillno, shein, billno, phase, request.RunId, now);

            // Quy kết tài khoản mua ngay ở bước ghi nhận — không chờ khớp đơn sàn.
            // Trước đây chỉ stamp khi Attach thành công nên hàng đợi "Đơn NCC chưa gắn"
            // mất hết tên TK dù sync đã resolve được email billing.
            if (orderAccountId.HasValue && purchase.FulfilAccountId != orderAccountId)
            {
                purchase.FulfilAccountId = orderAccountId;
                purchase.FulfilCardId = null;
            }

            changed = true;

            if (!string.IsNullOrWhiteSpace(purchase.TrackingNo)
                && !string.Equals(
                    snapshot.TrackingNo?.Trim(),
                    purchase.TrackingNo.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                // Local SupplierPurchases tracking only (warehouse or carrier) — not 4Seller publish.
                result.UpdatedTracking++;
            }

            // Billno lần đầu: xếp hàng get_order_info ngay và **hoãn khớp** đến phase detail.
            // List thiếu order_type/breakdown — khớp sớm dễ gắn Club rồi gỡ, hoặc tạo CostUpdate
            // khi giá còn sparse. unpaid/terminal rõ từ list không vào đây (ShouldQueue… = false).
            if (phase == "list"
                && ShouldQueueFirstSeenDetail(snapshot.Existed, purchase, shein))
            {
                result.NeedsDetailBillnos.Add(billno);
                result.NeedsDetailReasons[billno] = SheinNeedsDetailReasons.FirstSeen;
                AddSheinSkip(
                    result,
                    billno,
                    SheinSkipReasons.AwaitingDetail,
                    "chờ get_order_info lần đầu rồi khớp");
                result.Notes.Add(
                    $"SHEIN {billno}: lần đầu — chờ chi tiết rồi khớp (không gắn từ list sparse).");
                continue;
            }

            // Người xử lý đã quyết dòng này không thuộc đơn nào (Bỏ qua / ignored còn lại từ dữ liệu
            // cũ). Quyết định đó phải bền: không có chốt ở đây thì lượt sync kế tiếp tự gắn lại.
            // Payload vẫn được ghi nhận ở trên; chỉ phần khớp bị chặn — mở lại bằng "Trả về hàng đợi".
            // Xoá dòng chi trên sổ cái giờ xóa hẳn lần mua (không còn đi vào ignored).
            if (purchase.MatchState == SupplierPurchaseMatchStates.Ignored)
            {
                AddSheinSkip(
                    result,
                    billno,
                    purchase.SkipReason ?? SheinSkipReasons.OperatorIgnored,
                    "người xử lý đã bỏ qua");
                continue;
            }

            // SHEIN Club / Quarterly membership / virtual goods — same shipping address as
            // account owner, so phone+zip would falsely match a TikTok dropship order.
            //
            // Cờ đọc từ dòng purchase, không đánh giá lại từ payload: order/list không có
            // `order_type` nên phase list kết luận "hàng thật" rồi gắn vào một đơn sàn, phase detail
            // gỡ ra — lặp lại ở mọi lượt sync, mỗi vòng ghi rồi xoá một dòng sổ cái.
            if (purchase.IsMembershipOrVirtual)
            {
                var unlinked = await DetachPurchaseFromOrderAsync(pool, purchase, SheinSkipReasons.MembershipVirtual, now, cancellationToken);
                if (unlinked is not null)
                {
                    // Cùng lý do với nhánh ghost: FulfilAccountId của đơn được sync suy ra từ email
                    // billing của chính billno vừa gỡ, để lại thì đơn vẫn hiện "do tài khoản X mua"
                    // dù không còn billno nào của X.
                    SyncFulfilAttributionWithPurchases(unlinked);
                    await ReconcileCostUpdateTransactionsAsync(
                        unlinked, actingUserId, paidByOverride: unlinked.PaidByUserId, cancellationToken);
                    result.Notes.Add(
                        $"SHEIN {billno}: Club/Saver/virtual — đã gỡ khỏi đơn"
                        + $" {unlinked.PlatformOrderId ?? unlinked.FourSellerOrderId.ToString()}.");
                }
                else
                {
                    SupplierPurchaseIngest.MarkUnmatched(
                        purchase, SheinSkipReasons.MembershipVirtual, null, now);
                    result.Notes.Add(
                        $"SHEIN {billno}: Club/Saver/virtual — bỏ qua, không khớp đơn sàn.");
                }

                AddSheinSkip(
                    result,
                    billno,
                    SheinSkipReasons.MembershipVirtual,
                    unlinked is not null
                        ? "đã gỡ 1 link sai"
                        : $"order_type={purchase.SupplierOrderType?.ToString() ?? "?"}");
                continue;
            }

            // Đơn SHEIN chưa thanh toán không được đồng bộ, bất kể trạng thái. Chưa có đồng nào rời
            // tài khoản nên mọi thứ suy ra từ nó — giá mua, mốc "Mua", dòng chi trong sổ cái — đều
            // là số bịa. SHEIN cho tạo đơn trước rồi mới thu tiền và tự huỷ khi hết
            // order_expire_time (~4 giờ) nếu không ai trả; đơn còn trong hạn chờ trả tiền cũng chưa
            // phải một lần mua. Trả tiền rồi thì lượt sync sau tự gắn lại bằng clock pay_time thật,
            // nên bỏ qua ở đây không mất gì.
            if (IsUnpaidGhostBillno(purchase, shein))
            {
                var ghostOrder = await DetachPurchaseFromOrderAsync(pool, purchase, SheinSkipReasons.UnpaidGhost, now, cancellationToken, markGhost: true);
                if (ghostOrder is not null)
                {
                    SyncFulfilAttributionWithPurchases(ghostOrder);
                    await ReconcileCostUpdateTransactionsAsync(
                        ghostOrder,
                        actingUserId,
                        paidByOverride: ghostOrder.PaidByUserId,
                        cancellationToken);
                }
                else
                {
                    purchase.SupplierUnpaid = true;
                    SupplierPurchaseIngest.MarkUnmatched(
                        purchase, SheinSkipReasons.UnpaidGhost, null, now);
                }

                AddSheinSkip(
                    result,
                    billno,
                    SheinSkipReasons.UnpaidGhost,
                    ghostOrder is not null ? "đã gỡ khỏi 1 đơn local" : null);
                result.Notes.Add(
                    $"SHEIN {billno}: chưa thanh toán (pay_time = 0) — không đồng bộ"
                    + (ghostOrder is not null ? ", đã gỡ khỏi 1 đơn local." : "."));
                continue;
            }

            // Refunded / canceled (packageState=canceled, status refunded…): tiền đã hoàn về tài
            // khoản mua hàng nên không được tính sổ cái và không được giữ trên đơn TikTok — người đi
            // đơn cần mua lại từ tài khoản SHEIN khác. Upsert phía trên đã ghi trạng thái terminal
            // lên dòng mua; ở đây gỡ khỏi đơn (nếu đang gắn), xoá dòng CostUpdate, rồi bỏ qua khớp.
            // Trước đây billno terminal đã gắn vẫn refresh và còn nằm trong SelectCharged fallback
            // (giữ lần checkout cuối), nên GSU1RN24R00MBB7 / GSU1RN24R00MBRU vẫn trừ sổ cái dù đã hoàn.
            var sheinStatusGroup = NormalizeSheinStatusGroup(shein.SheinStatusGroup);
            // Incoming group refunded nhưng raw status=5 / title Shipped = chrome
            // Return&Refund, không phải hoàn thật — không gỡ lần mua lại còn sống.
            if (IsTerminalSupplierStatus(sheinStatusGroup) && !SheinLiveShipment.LooksLive(shein))
            {
                var previousOrderId = purchase.OrderId;
                var unlinked = await DetachPurchaseFromOrderAsync(pool, purchase, SheinSkipReasons.TerminalUnlinked, now, cancellationToken);
                if (unlinked is null && previousOrderId.HasValue)
                {
                    // Đơn ngoài pool lần này (Auto nông hẹp) — vẫn phải gỡ sổ cái, không thì dòng
                    // CostUpdate còn treo dù billno đã hoàn tiền.
                    unlinked = await _db.Orders
                        .Include(o => o.SupplierPurchases)
                        .FirstOrDefaultAsync(o => o.Id == previousOrderId.Value, cancellationToken);
                    if (unlinked is not null)
                    {
                        EnsureSupplierPurchases(unlinked);
                        RefreshOrderSupplierAggregates(unlinked);
                        OrderSheinMilestoneEvidence.ClearMilestonesWithoutLinkEvidence(unlinked);
                        unlinked.UpdatedAt = now;
                    }
                }

                if (unlinked is not null)
                {
                    SyncFulfilAttributionWithPurchases(unlinked);
                    await ReconcileCostUpdateTransactionsAsync(
                        unlinked, actingUserId, paidByOverride: unlinked.PaidByUserId, cancellationToken);
                    result.Notes.Add(
                        $"SHEIN {billno}: {sheinStatusGroup} — đã gỡ khỏi đơn"
                        + $" {unlinked.PlatformOrderId ?? unlinked.FourSellerOrderId.ToString()}"
                        + " (tiền hoàn, có thể mua lại).");
                }
                else
                {
                    SupplierPurchaseIngest.MarkUnmatched(
                        purchase, SheinSkipReasons.TerminalUnlinked, null, now);
                    result.Notes.Add(
                        $"SHEIN {billno}: {sheinStatusGroup} — bỏ qua, không khớp đơn sàn.");
                }

                AddSheinSkip(
                    result,
                    billno,
                    SheinSkipReasons.TerminalUnlinked,
                    previousOrderId.HasValue
                        ? $"đã gỡ · trạng thái {sheinStatusGroup}"
                        : $"trạng thái {sheinStatusGroup}");
                continue;
            }

            // Lọc theo tài khoản mua hàng chỉ đúng khi dữ liệu quy kết đã sạch. Chế độ "Cập nhật
            // tất cả" tồn tại để dọn dữ liệu bẩn, nên nó phải nhìn thấy cả đơn đang bị gán sai
            // FulfilAccountId — nếu không, chính đơn đang giữ billno bị loại khỏi pool, fast path
            // trượt và fuzzy chọn đơn khác rồi đâm vào unique index SupplierOrderNo.
            var matchPool = !isUpdateAll && orderAccountId.HasValue
                ? pool
                    .Where(o => o.FulfilAccountId == null || o.FulfilAccountId == orderAccountId)
                    .ToList()
                : pool;

            var sheinPhone = PhoneKey(shein.BuyerPhone);
            var sheinZip = ZipKey(shein.PostalCode);

            Order? match = null;
            var isAdditionalLink = false;

            // Fast path: dòng mua này đã thuộc một đơn nào đó. Đọc thẳng `purchase.OrderId` thay vì
            // quét pool — dòng mua là bản ghi toàn cục nên nó biết chủ của mình, kể cả khi đơn đó
            // nằm ngoài pool lần chạy này hoặc bị bộ lọc tài khoản loại ra.
            if (purchase.OrderId is not null)
            {
                match = pool.FirstOrDefault(o => o.Id == purchase.OrderId.Value);
                if (match is not null)
                {
                    // Refresh stubs / partial detail may omit address — trust existing billno link.
                    // Only reject when SHEIN sent a full phone+zip that conflicts with the local order.
                    var hasSheinAddress = sheinPhone.Length > 0 && sheinZip.Length > 0;
                    if (hasSheinAddress
                        && (sheinPhone != PhoneKey(match.BuyerPhone) || sheinZip != ZipKey(match.PostalCode)))
                    {
                        var conflictLabel = match.PlatformOrderId ?? match.FourSellerOrderId.ToString();

                        // Người xử lý đã tự chọn đơn này. Phone/zip lệch giữa sàn và NCC là chuyện
                        // thường khi khách sửa địa chỉ hoặc đặt hộ, và đó chính là ca máy khớp sai
                        // rồi người xử lý sửa tay — tước lại là xoá đúng cái kết luận của con người.
                        if (purchase.ManualLinkedAt is not null)
                        {
                            result.Notes.Add(
                                $"SHEIN {billno}: phone/zip lệch đơn {conflictLabel} nhưng link do người"
                                + " xử lý gắn tay — giữ nguyên.");
                        }
                        else if (!isUpdateAll)
                        {
                            MarkSheinUnmatchedForAddressRematch(
                                result,
                                billno,
                                SheinSkipReasons.AddressConflict,
                                $"đơn {conflictLabel}"
                                + $" · phone {(sheinPhone == PhoneKey(match.BuyerPhone) ? "khớp" : "lệch")}"
                                + $" · zip {(sheinZip == ZipKey(match.PostalCode) ? "khớp" : "lệch")}");
                            result.Notes.Add(
                                $"SHEIN {billno}: đã có mã NCC trên đơn {conflictLabel}"
                                + " nhưng phone/zip không khớp — bỏ qua.");
                            continue;
                        }
                        else
                        {
                            // Link sai địa chỉ chính là thứ chế độ "Cập nhật tất cả" phải sửa: bỏ qua
                            // ở đây thì billno kẹt vĩnh viễn trên đơn sai. Gỡ khỏi đơn cũ rồi khớp lại
                            // từ phone+zip+tên — dòng mua vẫn còn nguyên, chỉ đổi chủ.
                            result.Notes.Add(
                                $"SHEIN {billno}: mã NCC đang trên đơn {conflictLabel} nhưng phone/zip lệch"
                                + " — khớp lại (chế độ Cập nhật tất cả).");
                            await DetachPurchaseFromOrderAsync(pool, purchase, SheinSkipReasons.AddressConflict, now, cancellationToken);
                            ordersNeedingCostRefresh.Add(match.Id);
                            match = null;
                        }
                    }
                }
            }

            // New matches still require phone+zip; already-linked refresh may skip.
            if (match is null && (sheinPhone.Length == 0 || sheinZip.Length == 0))
            {
                var detail = sheinPhone.Length == 0 && sheinZip.Length == 0
                    ? "thiếu cả phone và zip"
                    : sheinPhone.Length == 0 ? "thiếu phone" : "thiếu zip";
                SupplierPurchaseIngest.MarkUnmatched(
                    purchase, SheinSkipReasons.MissingAddress, null, now);
                MarkSheinUnmatchedForAddressRematch(
                    result, billno, SheinSkipReasons.MissingAddress, detail);
                result.Notes.Add(
                    $"SHEIN {billno}: thiếu phone hoặc zip — bỏ qua (cần khớp chính xác cả hai).");
                continue;
            }

            if (match is null)
            {
                var (fuzzy, ambiguous) = FindBestOrderMatch(
                    shein, matchPool, resolveAmbiguity: isUpdateAll, claimedThisPass, purchaseMarks);
                if (ambiguous)
                {
                    SupplierPurchaseIngest.MarkUnmatched(
                        purchase,
                        SheinSkipReasons.AmbiguousMatch,
                        BuildSheinSkipEvidence(shein, pool, matchPool, sheinPhone, sheinZip, isUpdateAll, orderAccountId),
                        now);
                    AddSheinSkip(
                        result,
                        billno,
                        SheinSkipReasons.AmbiguousMatch,
                        "nhiều đơn cùng phone+zip, sku không phân giải được");
                    result.Notes.Add(
                        $"SHEIN {shein.Billno}: khớp nhiều đơn (cùng phone+zip, sku không phân giải được)"
                        + " — bỏ qua để tránh cập nhật sai.");
                    continue;
                }
                match = fuzzy;
            }

            // Additional Joint Shipment purchase: same phone+zip order already has other billnos.
            if (match is not null
                && !OrderHasSupplierPurchase(match, billno)
                && (match.SupplierPurchases?.Count ?? 0) > 0)
            {
                if (!CanAttachAdditionalPurchase(match, shein, out var attachReason))
                {
                    var (redirected, byMark) = FindSiblingWithRoom(
                        matchPool, shein, sheinPhone, sheinZip, billno, match, claimedThisPass, purchaseMarks);
                    if (redirected is null)
                    {
                        MarkSiblingLinkedSkip(result, purchase, billno, match, attachReason, now);
                        continue;
                    }

                    var reason = byMark ? "khớp dấu Đã mua" : "đơn cùng khách còn trống";
                    result.Notes.Add(
                        $"SHEIN {billno}: đơn {OrderLabel(match)} từ chối gắn thêm ({attachReason})"
                        + $" — chuyển sang đơn {OrderLabel(redirected)} ({reason}).");
                    match = redirected;
                    isAdditionalLink = (redirected.SupplierPurchases?.Count ?? 0) > 0;
                }
                else
                {
                    isAdditionalLink = true;
                }
            }

            if (match is null)
            {
                // Try phone+zip among already-linked candidates for additional attach.
                var sibling = pool.FirstOrDefault(o =>
                    PhoneKey(o.BuyerPhone) == sheinPhone
                    && ZipKey(o.PostalCode) == sheinZip
                    && !IsPlatformTerminalOrder(o)
                    && (o.SupplierPurchases?.Count ?? 0) > 0
                    && !OrderHasSupplierPurchase(o, billno)
                    && !IsPurchasedBeforePlatformOrder(shein, o));
                var siblingReason = "no-local";
                if (sibling is not null && CanAttachAdditionalPurchase(sibling, shein, out siblingReason))
                {
                    match = sibling;
                    isAdditionalLink = true;
                }
                else
                {
                    var (redirected, byMark) = FindSiblingWithRoom(
                        matchPool, shein, sheinPhone, sheinZip, billno, sibling, claimedThisPass, purchaseMarks);
                    if (redirected is not null)
                    {
                        if (sibling is not null)
                        {
                            var reason = byMark ? "khớp dấu Đã mua" : "đơn cùng khách còn trống";
                            result.Notes.Add(
                                $"SHEIN {billno}: đơn {OrderLabel(sibling)} từ chối gắn thêm ({siblingReason})"
                                + $" — chuyển sang đơn {OrderLabel(redirected)} ({reason}).");
                        }

                        match = redirected;
                        isAdditionalLink = (redirected.SupplierPurchases?.Count ?? 0) > 0;
                    }
                    else if (sibling is not null)
                    {
                        MarkSiblingLinkedSkip(result, purchase, billno, sibling, siblingReason, now);
                        continue;
                    }
                    else if (IsOnlyPlatformCanceledAfterPurchaseCandidate(
                        matchPool, shein, sheinPhone, sheinZip))
                    {
                        SupplierPurchaseIngest.MarkUnmatched(
                            purchase,
                            SheinSkipReasons.PlatformCanceled,
                            BuildSheinSkipEvidence(
                                shein, pool, matchPool, sheinPhone, sheinZip, isUpdateAll, orderAccountId),
                            now);
                        AddSheinSkip(
                            result,
                            billno,
                            SheinSkipReasons.PlatformCanceled,
                            "đơn sàn đã hủy/hoàn và mua sau khi hủy");
                        result.Notes.Add(
                            $"SHEIN {shein.Billno}: đơn sàn đã hủy/hoàn và mua sau khi hủy"
                            + " — vào hàng đợi Đơn NCC chưa gắn.");
                        continue;
                    }
                    else
                    {
                        // Có shipping_telephone+postcode rồi mà không có đơn local — rematch detail
                        // không giúp; không đưa vào UnmatchedBillnos (tránh detail N/N ảo).
                        //
                        // Dòng mua ở lại bảng với `MatchState='unmatched'` kèm bằng chứng near-miss:
                        // đây chính là 14 billno mà lượt trước chỉ để lại một con số trong nhật ký
                        // rồi tự xoá sau 7 ngày.
                        SupplierPurchaseIngest.MarkUnmatched(
                            purchase,
                            SheinSkipReasons.NoLocalMatch,
                            BuildSheinSkipEvidence(
                                shein, pool, matchPool, sheinPhone, sheinZip, isUpdateAll, orderAccountId),
                            now);
                        AddSheinSkip(
                            result,
                            billno,
                            SheinSkipReasons.NoLocalMatch,
                            $"pool {matchPool.Count}/{pool.Count} đơn"
                            // Chỉ nói "lọc theo tài khoản" khi bộ lọc thực sự chạy: mode all luôn
                            // dùng cả pool, và câu này từng dẫn người đọc đi sai hướng suốt.
                            + (!isUpdateAll && orderAccountId.HasValue
                                ? $" · lọc theo tài khoản {orderAccountName ?? orderAccountId.ToString()}"
                                : string.Empty));
                        result.Notes.Add(
                            $"SHEIN {shein.Billno}: không khớp đơn nào — vào hàng đợi Đơn NCC chưa gắn.");
                        continue;
                    }
                }
            }

            var alreadySyncedSameBillno = purchase.OrderId == match.Id;

            // Dòng mua đang thuộc một đơn khác. Không còn là ca "unique index chặn INSERT" như bảng
            // cũ — đổi chủ chỉ là gán lại OrderId — nhưng vẫn phải quyết định có được phép đổi không.
            if (!alreadySyncedSameBillno && purchase.OrderId is not null)
            {
                var holder = pool.FirstOrDefault(o => o.Id == purchase.OrderId.Value);
                var holderLabel = holder is not null
                    ? holder.PlatformOrderId ?? holder.FourSellerOrderId.ToString()
                    : purchase.OrderId.Value.ToString();

                // Khoá gắn tay thắng mọi chế độ: máy chỉ khớp được phone+zip+tên+SKU nên nó không
                // biết thứ người xử lý đã tra ra. Đơn còn trong pool thì refresh tiếp trên chính đơn
                // đó (tracking/trạng thái vẫn phải cập nhật), ngoài pool thì để nguyên lượt này.
                if (purchase.ManualLinkedAt is not null)
                {
                    if (holder is null)
                    {
                        AddSheinSkip(
                            result,
                            billno,
                            SheinSkipReasons.ManualLinkLocked,
                            $"giữ ở đơn {holderLabel} (ngoài pool lượt này)");
                        result.Notes.Add(
                            $"SHEIN {billno}: đã gắn tay vào đơn {holderLabel} — giữ nguyên, không khớp lại.");
                        continue;
                    }

                    result.Notes.Add(
                        $"SHEIN {billno}: đã gắn tay vào đơn {holderLabel} — giữ nguyên thay vì chuyển sang"
                        + $" đơn {OrderLabel(match)} vừa khớp.");
                    match = holder;
                    alreadySyncedSameBillno = true;
                    isAdditionalLink = false;
                }
                else if (!isUpdateAll)
                {
                    AddSheinSkip(
                        result,
                        billno,
                        SheinSkipReasons.DuplicateBillno,
                        $"đang thuộc đơn {holderLabel}");
                    result.Notes.Add(
                        $"SHEIN {billno}: đang gắn đơn {holderLabel} — bỏ qua"
                        + " (chế độ Chỉ đơn mới không tước billno của đơn khác).");
                    continue;
                }
                else
                {
                    var previousOrder = await DetachPurchaseFromOrderAsync(pool, purchase, SheinSkipReasons.DuplicateBillno, now, cancellationToken);
                    if (previousOrder is not null)
                    {
                        ordersNeedingCostRefresh.Add(previousOrder.Id);
                        changed = true;
                    }

                    result.Notes.Add(
                        $"SHEIN {billno}: thu hồi khỏi đơn {holderLabel}"
                        + " để gắn lại theo phone+zip+tên (chế độ Cập nhật tất cả).");
                }
            }

            // Mode "new": same billno already linked → incremental update only when delta
            // (tracking / status / total / wallet / missing terminal milestones); else optional account stamp.
            if (!isUpdateAll && alreadySyncedSameBillno && !isAdditionalLink)
            {
                var hasDelta = HasSheinIncrementalDelta(purchase, snapshot, shein, match);
                if (!hasDelta)
                    {
                    // Upsert phía trên đã áp payload lên dòng mua rồi, nên snapshot giá mua của đơn
                    // phải đi theo. "Không có delta" là kết luận về việc có cần chạy lại
                    // Attach/Reconcile hay không — không phải giấy phép để Charged* đứng lại ở giá
                    // trị cũ, và nhánh reconcile bên dưới còn đọc IsCharged ngay sau đây.
                    RefreshOrderSupplierAggregates(match);

                    // Vẫn xin detail khi snapshot (trước Upsert) nghi ví bị đổ vào coupon — nếu không
                    // thì chế độ Chỉ đơn mới bỏ qua mãi và cột Coupon/Wallet trên sổ cái lệch.
                    if (phase == "list"
                        && LooksLikeWalletMisattributed(
                            snapshot.SupplierTotal,
                            snapshot.SupplierCouponPrice,
                            snapshot.SupplierWalletPrice))
                    {
                        result.NeedsDetailBillnos.Add(billno);
                        result.NeedsDetailReasons[billno] = SheinNeedsDetailReasons.WalletCostStale;
                    }
                    else if (phase == "list"
                        && (purchase.SupplierSubtotal is null || purchase.SupplierTotal is null))
                    {
                        // Đã khớp từ list sparse: mode new không thấy delta tiền/status nên
                        // không bao giờ tới EvaluateSheinListNeedsDetail — breakdown kẹt
                        // "Thiếu order detail" (cả lô SHE-19 18/08).
                        result.NeedsDetailBillnos.Add(billno);
                        result.NeedsDetailReasons[billno] = SheinNeedsDetailReasons.CostIncomplete;
                    }

                    if (orderAccountId.HasValue && match.FulfilAccountId is null)
                    {
                        // Bản sao cấp đơn của quy kết luôn suy từ lần mua — một luật, một chỗ.
                        // Chạy trước ApplyPurchasePaymentAccountAsync: hàm đó so tài khoản của lần
                        // mua với tài khoản của đơn để quyết định có tái dùng thẻ cấp đơn hay không.
                        SyncFulfilAttributionWithPurchases(match);
                        match.PaidByUserId ??= ResolvePurchaseBuyer(
                            match, purchase, purchaseMarks, now);
                        await ApplyPurchasePaymentAccountAsync(
                            match, purchase, orderAccountId, cancellationToken);
                        await ReconcileCostUpdateTransactionsAsync(
                            match, actingUserId, paidByOverride: match.PaidByUserId, cancellationToken);
                        match.UpdatedAt = now;
                        changed = true;
                        result.Matched++;
                        result.Notes.Add(
                            $"SHEIN {billno}: đã có mã NCC — chỉ link tài khoản mua hàng"
                            + (orderAccountName is null ? "" : $" ({orderAccountName})")
                            + " (chế độ Chỉ đơn mới).");
                    }
                    else
                    {
                        result.Unchanged++;
                        result.Notes.Add(
                            $"SHEIN {billno}: đã có mã NCC — không đổi (chế độ Chỉ đơn mới).");
                    }
                    continue;
                }
            }

            if (!alreadySyncedSameBillno)
            {
                while (!await HasRoomForPurchaseInDbAsync(match, purchase, cancellationToken))
                {
                    if (CanAttachAdditionalPurchase(match, shein, out var overCapacityReason)
                        && (overCapacityReason is "shared-relation-billno" or "shared-confluence-batch"))
                    {
                        break;
                    }

                    var (next, byMark) = FindSiblingWithRoom(
                        matchPool, shein, sheinPhone, sheinZip, billno, match, claimedThisPass, purchaseMarks);
                    if (next is null)
                    {
                        MarkSiblingLinkedSkip(
                            result, purchase, billno, match, OrderCapacityFullReason, now);
                        match = null;
                        break;
                    }

                    var reason = byMark ? "khớp dấu Đã mua" : "đơn cùng khách còn trống";
                    result.Notes.Add(
                        $"SHEIN {billno}: đơn {OrderLabel(match)} vừa hết chỗ (lượt khác đã gắn)"
                        + $" — chuyển sang đơn {OrderLabel(next)} ({reason}).");
                    match = next;
                    isAdditionalLink = (next.SupplierPurchases?.Count ?? 0) > 0;
                }

                if (match is null) continue;
            }

            result.Matched++;

            var purchasedSet = false;
            var accountLinked = false;
            var txnUpserted = false;
            var supplierPricesSet = false;
            var billnoLinked = !alreadySyncedSameBillno;
            var prevLinkTotal = snapshot.SupplierTotal;
            if (orderAccountId.HasValue && match.FulfilAccountId is null)
            {
                match.FulfilAccountId = orderAccountId;
                accountLinked = true;
            }

            // Mua chỉ stamp từ clock đặt hàng SHEIN thật — không bao giờ giờ chạy sync.
            // Thiếu orderedAt ⇒ để trống (UI hiện chấm rỗng + ?); lần detail sau sẽ điền.
            //
            // "Mua" là mốc tiền rời tài khoản, không phải mốc tạo đơn. SHEIN cho tạo đơn trước rồi
            // mới thu tiền, và tự huỷ khi hết order_expire_time nếu không ai trả — đơn ghost đó có
            // addTime nhưng pay_time = 0, stamp theo addTime sẽ báo "Đã mua" cho một lần mua không
            // hề xảy ra. Ưu tiên pay_time; chỉ lùi về addTime khi payload không kết luận được.
            var purchasedAt = shein.SheinPaidAt
                ?? (shein.SheinUnpaid == true ? null : shein.OrderedAt);
            if (purchasedAt.HasValue)
            {
                var before = match.FulfilPurchasedAt;
                OrderFulfilTimeline.Stamp(
                    match,
                    OrderFulfilTimeline.Milestone.FulfilPurchased,
                    NormalizeUtc(purchasedAt.Value),
                    OrderFulfilTimeline.Source.Shein);
                if (match.FulfilPurchasedAt != before) purchasedSet = true;
            }

            var link = AttachSupplierPurchase(match, purchase, shein, now, actingUserId);
            // Người mua = dấu Đã mua / attempt — không bao giờ lấy người chạy sync.
            match.PaidByUserId ??= ResolvePurchaseBuyer(match, purchase, purchaseMarks, now);
            if (billnoLinked || isAdditionalLink)
            {
                claimedThisPass.Add(match.Id);
            }

            // Một đơn nhiều billno chỉ gửi một tin: TryAdd theo Order.Id.
            // Đã gửi Add track thì bỏ qua (kể cả transition giả do race).
            if (match.TelegramAddTrackNotifiedAt is null
                && ShouldNotifyDetailAddTrack(request.RunId, snapshot, link))
            {
                detailAddTrackWatchlist.TryAdd(
                    match.Id, new SheinDetailAddTrackNotifyItem(match, link, billno, now));
            }

            await ApplyPurchasePaymentAccountAsync(
                match, purchase, orderAccountId, cancellationToken);
            // Bản sao cấp đơn của quy kết luôn suy từ lần mua. Không thừa với nhánh gán ở trên:
            // nhánh đó chỉ chạy khi email billing của lượt này resolve được tài khoản, còn lần mua
            // có thể đã mang tài khoản từ lượt trước — đơn không được trống cột «TK mua» vì thế.
            SyncFulfilAttributionWithPurchases(match);
            if (phase == "detail") supplierPricesSet = true;
            // Cả hai phase gắn TrackingKind=carrier khi list/detail đã có shipping_no.
            // Auto Mark Ship lên 4Seller không chạy trên HTTP extension — job
            // shein.order_detail_sync / Sync Track chọn qua PendingQueuePredicate + ShouldAttempt.
            if (OrderAutoMarkShipPolicy.ShouldAttempt(match, link, now))
            {
                autoMarkShipCandidates.Add(new AutoMarkShipCandidate(
                    match,
                    link,
                    billno,
                    // Không lấy SheinShippedAt: đó là mốc rời kho (mốc Transit), dùng cho Track sẽ
                    // làm hai cột trùng nhau. Auto Mark Ship đẩy mã lên sàn ngay lúc này nên khi
                    // SHEIN không có clock riêng, thời điểm đẩy mới là mốc Track đúng.
                    shein.SheinTrackingAt));
            }

            // Reconcile chỉ khi caller đã opt-in Mark Ship (xem runAutoMarkShip phía dưới).
            // Vẫn thu candidate ở đây để lượt sweep/Sync Track không bỏ sót unconfirmed.
            if (OrderAutoMarkShipPolicy.NeedsReconcile(link)
                || string.Equals(
                    link.AutoMarkShipStatus,
                    AutoMarkShipStatuses.Unconfirmed,
                    StringComparison.Ordinal))
            {
                autoMarkShipReconcileCandidates.Add(new AutoMarkShipCandidate(
                    match, link, billno, shein.SheinTrackingAt));
            }

            var beforeCard = match.FulfilCardId;
            var ledgerChanged = await ReconcileCostUpdateTransactionsAsync(
                match, actingUserId, paidByOverride: match.PaidByUserId, cancellationToken);
            if (ledgerChanged)
            {
                txnUpserted = true;
                if (match.FulfilCardId != beforeCard) changed = true;
            }

            var costIncomplete = link.SupplierSubtotal is null || link.SupplierTotal is null;
            if (costIncomplete && !string.IsNullOrWhiteSpace(billno))
            {
                result.IncompleteCostBillnos.Add(billno);
            }

            if (phase == "list")
            {
                var needsDetailReason = EvaluateSheinListNeedsDetail(
                    match,
                    link,
                    shein,
                    billnoLinked: billnoLinked || isAdditionalLink,
                    prevLinkTotal: prevLinkTotal,
                    forceDetail: isUpdateAll);
                if (needsDetailReason is not null)
                {
                    result.NeedsDetailBillnos.Add(billno);
                    // Total lệch là lý do duy nhất cần con số để đối chiếu payload với DB.
                    result.NeedsDetailReasons[billno] =
                        needsDetailReason == SheinNeedsDetailReasons.TotalDelta
                            ? $"{needsDetailReason}:{prevLinkTotal}→{shein.Total}"
                            : needsDetailReason;
                }
            }

            if (supplierPricesSet) result.UpdatedCost++;

            match.UpdatedAt = now;
            changed = true;
            result.Notes.Add(
                $"SHEIN {shein.Billno} → đơn {match.PlatformOrderId ?? match.FourSellerOrderId.ToString()}"
                + $" · {phase}"
                + (isAdditionalLink ? " · +link joint" : billnoLinked ? " · link orderNo" : string.Empty)
                + (accountLinked ? " · link account" : string.Empty)
                + (txnUpserted ? " · txn" : string.Empty)
                + (purchasedSet ? " · fulfil mua" : string.Empty)
                + (link.TrackingKind == SupplierTrackingKinds.Carrier
                    ? " · carrier detected"
                    : link.TrackingKind == SupplierTrackingKinds.Warehouse ? " · warehouse tracking" : string.Empty)
                + (supplierPricesSet ? " · NCC" : string.Empty)
                + $" · links×{(match.SupplierPurchases?.Count ?? 0)}");
        }

        var rebalanced = await RebalanceOverfilledOrdersAsync(
            pool,
            result,
            ordersNeedingCostRefresh,
            claimedThisPass,
            actingUserId,
            now,
            cancellationToken,
            purchaseMarks);
        if (rebalanced > 0) changed = true;

        var relocated = await RelocatePurchasesOffCanceledOrdersAsync(
            pool,
            result,
            ordersNeedingCostRefresh,
            claimedThisPass,
            actingUserId,
            now,
            cancellationToken);
        if (relocated > 0) changed = true;

        // Mode all may have cleared links (incl. membership) before rematch — reconcile CostUpdate.
        foreach (var orderId in ordersNeedingCostRefresh)
        {
            var order = pool.FirstOrDefault(o => o.Id == orderId);
            if (order is null) continue;
            await ReconcileCostUpdateTransactionsAsync(
                order, actingUserId, paidByOverride: order.PaidByUserId, cancellationToken);
            changed = true;
        }

        var saveFailed = false;
        if (changed)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Unique index SupplierOrderNo là ràng buộc duy nhất có thể vỡ ở đây, và một billno
                // đụng nhau không được phép biến cả lượt sync thành lỗi 500. Ghi lý do vào kết quả
                // rồi đi tiếp tới run log để còn dấu vết chẩn đoán; lượt sync sau tự thử lại.
                saveFailed = true;
                _logger.LogWarning(
                    ex,
                    "SHEIN sync {Phase} mode {Mode} không lưu được lô {RunId}/{BatchIndex}",
                    phase,
                    modeRaw,
                    request.RunId,
                    request.BatchIndex);
                _db.ChangeTracker.Clear();

                // Không dòng nào xuống DB, nên mọi con số đếm phần đã ghi đều là số ảo. Để nguyên
                // chúng thì cả kết quả trả về extension và nhật ký lượt đều báo lô này thành công,
                // và người đọc log sẽ không hiểu vì sao đơn vẫn thiếu tiền.
                result.SaveFailed = true;
                result.UpdatedTracking = 0;
                result.UpdatedCost = 0;
                result.AutoShipped = 0;
                result.AutoShipFailed = 0;
                result.AutoShipBlocked = 0;
                result.Notes.Add(
                    "Không lưu được lô này (xung đột mã NCC trùng trong DB) — hãy chạy lại"
                    + " ở chế độ Cập nhật tất cả để thu hồi mã cũ.");
            }
        }

        // Auto Mark Ship chỉ cho caller nội bộ (detail sweep / Sync Track) đã set
        // MaxAutoMarkShipAttempts hoặc AutoMarkShipPollInteractive. Extension HTTP +
        // SheinRealtimeSyncService để null → chỉ ingest; Mark Ship do job nền.
        var runAutoMarkShip = OrderAutoMarkShipPolicy.ShouldRunAutoMarkShip(
            request.MaxAutoMarkShipAttempts,
            request.AutoMarkShipPollInteractive);

        if (!saveFailed)
        {
            if (runAutoMarkShip && autoMarkShipReconcileCandidates.Count > 0)
            {
                await ProcessAutoMarkShipReconcileAsync(
                    autoMarkShipReconcileCandidates, result, cancellationToken);
            }

            if (runAutoMarkShip && autoMarkShipCandidates.Count > 0)
            {
                await ProcessAutoMarkShipCandidatesAsync(
                    autoMarkShipCandidates,
                    result,
                    request.MaxAutoMarkShipAttempts,
                    request.AutoMarkShipPollInteractive,
                    cancellationToken);
            }

            // Mark Ship vừa chạy xong: xếp lại đơn pending từ lượt trước (transition-only không
            // còn khớp ShouldNotifyDetailAddTrack vì snapshot đã carrier).
            if (request.RunId?.StartsWith("detail-", StringComparison.Ordinal) == true)
            {
                EnqueuePendingDetailAddTrackRetries(pool, detailAddTrackWatchlist, DateTime.UtcNow);
            }

            if (detailAddTrackWatchlist.Count > 0)
            {
                await TryNotifySheinDetailAddTrackAsync(
                    [.. detailAddTrackWatchlist.Values], cancellationToken);
            }
        }

        if (!saveFailed)
        {
            // Realtime USPS / warehouse track notify (dedup via Telegram*NotifiedAt stamps).
            var handlingCandidates = pool
                .Where(o =>
                    (o.TelegramUspsNotifiedAt is null
                     && OrderFulfilHandlingSignals.HasUspsUnprocessed(o))
                    || (o.TelegramWarehouseNotifiedAt is null
                        && OrderFulfilHandlingSignals.NeedsThankCard(o)))
                .ToList();
            if (handlingCandidates.Count > 0)
            {
                await TryNotifyFulfilHandlingAsync(handlingCandidates, cancellationToken);
            }
        }

        if (result.NeedsDetailBillnos.Count > 1)
        {
            result.NeedsDetailBillnos = result.NeedsDetailBillnos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (result.IncompleteCostBillnos.Count > 1)
        {
            result.IncompleteCostBillnos = result.IncompleteCostBillnos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (result.UnmatchedBillnos.Count > 1)
        {
            result.UnmatchedBillnos = result.UnmatchedBillnos
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        await WriteSheinSyncRunLogAsync(
            request,
            result,
            phase,
            modeRaw,
            lookbackDays,
            pool.Count,
            startedAt,
            cancellationToken);

        return result;
    }


    /// <summary>Điểm cộng khi sku sản phẩm trùng — lớn hơn tổng name+street+city+state.</summary>
    private const int SkuMatchScore = 8;

    /// <summary>
    /// Điểm cộng khi đơn còn chỗ cho một lần mua nữa (xem <see cref="HasRoomForPurchase"/>).
    ///
    /// Nhỏ hơn <see cref="SkuMatchScore"/> một cách có chủ ý: "đơn này còn trống" chỉ là suy đoán
    /// từ số dòng hàng, còn sku trùng là bằng chứng trực tiếp billno chứa đúng hàng của đơn. Đặt
    /// bằng hoặc lớn hơn sẽ khiến một đơn anh em trống cướp billno của đơn joint thật sự.
    /// </summary>
    private const int PurchaseRoomScore = 4;

    /// <summary>
    /// Picks the local order that best matches a SHEIN order using exact Shipping keys from
    /// <c>shippingaddr_info</c>: <c>shipping_telephone</c> + <c>shipping_postcode</c> (mandatory
    /// after digit normalization) and <c>shipping_firstname</c>/<c>shipping_lastname</c> for
    /// ranking, plus SKU overlap. Does <b>not</b> score street/city/state or invent field aliases.
    /// Returns <c>ambiguous=true</c> when more than one order shares the same top score.
    ///
    /// <paramref name="resolveAmbiguity"/> (chế độ "Cập nhật tất cả") phân giải thế hoà bằng tên
    /// người nhận rồi khoảng cách thời gian đặt hàng, thay vì bỏ qua billno. Bỏ qua là lựa chọn
    /// đúng khi chỉ cần thêm đơn mới, nhưng khi dựng lại toàn bộ dữ liệu thì một khách đặt hai lần
    /// cùng địa chỉ sẽ khoá vĩnh viễn cả hai đơn.
    ///
    /// Thế hoà quyết được bằng "đơn nào còn chỗ" thì phân giải ở <b>mọi</b> chế độ — xem
    /// <see cref="ResolveTiedByPurchaseDemand"/>.
    /// </summary>
    internal static (Order? Match, bool Ambiguous) FindBestOrderMatch(
        SheinOrderDto shein,
        IReadOnlyList<Order> pool,
        bool resolveAmbiguity = false,
        IReadOnlyCollection<Guid>? claimedThisPass = null,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        // Match keys from SHEIN shippingaddr_info only (exact):
        // shipping_telephone → BuyerPhone, shipping_postcode → PostalCode,
        // shipping_firstname + shipping_lastname → BuyerName. No street/city/state guessing.
        var sheinName = NameKey(shein.BuyerName);
        var sheinPhone = PhoneKey(shein.BuyerPhone);
        var sheinZip = ZipKey(shein.PostalCode);
        var sheinProductIds = BuildSheinProductIdKeys(shein);

        // Hard gate: shipping_telephone + shipping_postcode (normalized digits).
        if (sheinPhone.Length == 0 || sheinZip.Length == 0)
        {
            return (null, false);
        }

        // Thu thập ứng viên hợp lệ rồi phân tầng: đơn còn hiệu lực luôn thắng đơn đã hủy/hoàn.
        // Hai đơn cùng khách + cùng SKU mà một đã CANCELLED từng khiến billno đổ vào đơn hủy.
        var eligible = new List<Order>();
        foreach (var order in pool)
        {
            if (sheinPhone != PhoneKey(order.BuyerPhone)) continue;
            if (sheinZip != ZipKey(order.PostalCode)) continue;
            // Khách mua lặp lại về cùng địa chỉ nên phone+zip trùng cả những đơn đặt sau lần mua
            // này; hàng mua trước khi đơn tồn tại không thể là hàng của đơn đó.
            if (IsPurchasedBeforePlatformOrder(shein, order)) continue;
            // Mua sau khi đơn đã hủy → không gắn vào đơn đã đóng.
            if (IsPurchasedAfterPlatformCancel(shein, order)) continue;
            eligible.Add(order);
        }

        var scoring = eligible.Where(o => !IsPlatformTerminalOrder(o)).ToList();
        if (scoring.Count == 0) scoring = eligible;

        Order? best = null;
        var bestScore = 0;
        var topScored = new List<Order>();

        foreach (var order in scoring)
        {
            // Phone+zip matched — rank by shipping first/last name + SKU only.
            var score = 10;
            var skuOverlap = CountProductIdOverlap(sheinProductIds, order);
            if (skuOverlap > 0) score += SkuMatchScore + Math.Min(skuOverlap - 1, 4);
            if (sheinName.Length > 0 && sheinName == NameKey(order.BuyerName)) score += 2;
            // Khách đặt hai đơn riêng về cùng địa chỉ trùng nhau ở mọi khoá khớp; số chỗ còn lại là
            // tín hiệu duy nhất tách được chúng, nếu không thì mọi billno đổ vào cùng một đơn.
            if (HasRoomForPurchase(order)
                && (claimedThisPass is null || !claimedThisPass.Contains(order.Id)))
            {
                score += PurchaseRoomScore;
            }

            if (score > bestScore)
            {
                best = order;
                bestScore = score;
                topScored.Clear();
                topScored.Add(order);
            }
            else if (score == bestScore)
            {
                topScored.Add(order);
            }
        }

        if (best is null) return (null, false);
        if (topScored.Count <= 1) return (best, false);

        // Thế hoà quyết được bằng "đơn nào còn chỗ" thì phân giải ở mọi chế độ: gắn vào đơn trống
        // không tước billno của ai. Các thế hoà còn lại vẫn chỉ resolve khi mode all.
        var byDemand = ResolveTiedByPurchaseDemand(topScored, shein, purchaseMarks);
        if (byDemand is not null) return (byDemand, false);
        if (!resolveAmbiguity) return (null, true);

        var resolved = ResolveTiedOrderMatch(shein, sheinName, topScored, purchaseMarks);
        return resolved is null ? (null, true) : (resolved, false);
    }

    /// <summary>
    /// Chọn một đơn trong nhóm cùng điểm cao nhất: ưu tiên đơn có tên người nhận trùng, rồi đơn có
    /// <c>OrderCreateTime</c> gần thời điểm đặt hàng SHEIN nhất. Không có mốc thời gian nào để so
    /// thì trả <c>null</c> — thà bỏ qua còn hơn gắn tiền vào đơn đoán bừa.
    /// </summary>
    private static Order? ResolveTiedOrderMatch(
        SheinOrderDto shein,
        string sheinName,
        List<Order> tied,
        PurchaseMarkIndex? purchaseMarks = null)
    {
        var byDemand = ResolveTiedByPurchaseDemand(tied, shein, purchaseMarks);
        if (byDemand is not null) return byDemand;

        var candidates = tied;
        if (sheinName.Length > 0)
        {
            var byName = tied.Where(o => NameKey(o.BuyerName) == sheinName).ToList();
            if (byName.Count == 1) return byName[0];
            if (byName.Count > 1) candidates = byName;
        }

        var sheinOrderedAt = shein.SheinPaidAt ?? shein.OrderedAt;
        if (sheinOrderedAt is null) return null;

        var reference = NormalizeUtc(sheinOrderedAt.Value);
        var ranked = candidates
            .Where(o => o.OrderCreateTime.HasValue)
            .Select(o => (Order: o, Distance: Math.Abs((NormalizeUtc(o.OrderCreateTime!.Value) - reference).Ticks)))
            .OrderBy(x => x.Distance)
            .ToList();
        if (ranked.Count == 0) return null;
        if (ranked.Count > 1 && ranked[0].Distance == ranked[1].Distance) return null;
        return ranked[0].Order;
    }

    /// <summary>
    /// Biên dưới của pool khớp mode <c>all</c>: lùi từ mốc mua sớm nhất của lô đủ xa để gồm đơn
    /// sàn đã đặt trước lần mua (lead time fulfil), chặn trên <see cref="SheinMatchPoolMaxAgeDays"/>.
    /// </summary>
    /// <param name="earliestPurchase">Mốc mua / đặt SHEIN sớm nhất trong lô (UTC).</param>
    /// <param name="lookbackDays">Lookback từ Settings — dùng làm số ngày lùi khi lớn hơn tối thiểu.</param>
    /// <param name="utcNow">Đồng hồ UTC của lần chạy (inject để test).</param>
    internal static DateTime ComputeSheinMatchPoolSince(
        DateTime earliestPurchase,
        int lookbackDays,
        DateTime utcNow)
    {
        var leadDays = Math.Max(SheinMatchPoolMinLeadDays, lookbackDays);
        var fromPurchase = earliestPurchase.AddDays(-leadDays);
        var floor = utcNow.AddDays(-SheinMatchPoolMaxAgeDays);
        return fromPurchase > floor ? fromPurchase : floor;
    }

    /// <summary>
    /// Đơn fulfil có <c>OrderCreateTime</c> trong cửa sổ lead (hoặc chưa có clock) — ứng viên khớp
    /// mới của mode <c>new</c>. Đơn đã gắn billno của lô được nạp riêng, không đi qua hàm này.
    /// </summary>
    internal static bool IsNewModeFulfilWindowCandidate(DateTime? orderCreateTime, DateTime poolSince)
        => orderCreateTime is null || orderCreateTime >= poolSince;

    /// <summary>Id sản phẩm trong URL PDP SHEIN (<c>a-p-86020798.html</c>).</summary>
    private static readonly Regex SheinPdpIdRegex =
        new(@"-p-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Tập product id / sku của đơn SHEIN, so không phân biệt hoa thường.</summary>
    internal static HashSet<string> BuildSheinProductIdKeys(SheinOrderDto shein)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in shein.ProductIds)
        {
            var value = raw?.Trim();
            if (!string.IsNullOrEmpty(value)) keys.Add(value);
        }
        return keys;
    }

    /// <summary>
    /// Số line item của đơn local có sku trùng đơn SHEIN. Sản phẩm nhiều variant sinh nhiều sku
    /// nên chỉ cần trùng một khóa là cùng sản phẩm.
    /// </summary>
    private static int CountProductIdOverlap(HashSet<string> sheinProductIds, Order order)
    {
        if (sheinProductIds.Count == 0) return 0;

        var covered = CoveredProductKeys(order);
        var matched = 0;
        foreach (var item in order.Items)
        {
            var keys = OrderItemProductIdKeys(item).ToList();
            if (keys.Count == 0) continue;
            if (!keys.Any(sheinProductIds.Contains)) continue;
            // Dòng hàng đã được một lần mua còn hiệu lực phủ thì không còn là bằng chứng cho billno
            // mới: giữ nó lại làm đơn đã đủ hàng luôn thắng đơn anh em còn trống của cùng khách,
            // đúng cái đẩy mọi billno vào một đơn. Ca joint thật (đơn 2 dòng, mới phủ 1) vẫn còn
            // dòng chưa phủ nên vẫn giữ điểm sku cao.
            if (keys.Any(covered.Contains)) continue;
            matched++;
        }
        return matched;
    }

    /// <summary>
    /// Khóa nhận dạng sản phẩm của một line item: <c>SellerSku</c> đầy đủ, goods id phía trước dấu
    /// <c>-</c> (<c>86020798-I58rjh8y8izh</c> → <c>86020798</c>), sku code phía sau, và id trong
    /// <c>SourceUrl</c>.
    /// </summary>
    private static IEnumerable<string> OrderItemProductIdKeys(OrderItem item)
    {
        var sku = item.SellerSku?.Trim();
        if (!string.IsNullOrEmpty(sku))
        {
            yield return sku;
            var dash = sku.IndexOf('-');
            if (dash > 0)
            {
                yield return sku[..dash];
                if (dash + 1 < sku.Length) yield return sku[(dash + 1)..];
            }
        }

        if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            var m = SheinPdpIdRegex.Match(item.SourceUrl);
            if (m.Success) yield return m.Groups[1].Value;
        }
    }

    /// <inheritdoc />
    public async Task<SheinSyncConfigDto> GetSheinSyncConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [
                ExtensionSheinSyncSettingKeys.Enabled,
                ExtensionSheinSyncSettingKeys.IntervalValue,
                ExtensionSheinSyncSettingKeys.IntervalUnit,
                ExtensionSheinSyncSettingKeys.UpdateMode,
                ExtensionSheinSyncSettingKeys.ConcurrentThreads,
                ExtensionSheinSyncSettingKeys.RefreshLookbackDays,
                ExtensionSheinSyncSettingKeys.RefreshMaxBillnos,
                ExtensionSheinSyncSettingKeys.CaptureRawPayload,
                ExtensionSheinSyncSettingKeys.ForceBillnos
            ],
            cancellationToken);

        var enabled = values.TryGetValue(ExtensionSheinSyncSettingKeys.Enabled, out var e)
            && (string.Equals(e, "true", StringComparison.OrdinalIgnoreCase) || e == "1");

        var value = 60;
        if (values.TryGetValue(ExtensionSheinSyncSettingKeys.IntervalValue, out var v)
            && int.TryParse(v, out var parsed) && parsed > 0)
        {
            value = parsed;
        }

        var unit = values.TryGetValue(ExtensionSheinSyncSettingKeys.IntervalUnit, out var u)
            ? u
            : ExtensionSheinSyncSettingKeys.UnitSeconds;

        var seconds = string.Equals(unit, ExtensionSheinSyncSettingKeys.UnitMinutes, StringComparison.OrdinalIgnoreCase)
            ? value * 60
            : value;

        var concurrentThreads = ExtensionSheinSyncSettingKeys.DefaultConcurrentThreads;
        if (values.TryGetValue(ExtensionSheinSyncSettingKeys.ConcurrentThreads, out var threadsRaw)
            && int.TryParse(threadsRaw, out var threadsParsed) && threadsParsed > 0)
        {
            concurrentThreads = threadsParsed;
        }

        concurrentThreads = Math.Clamp(
            concurrentThreads,
            ExtensionSheinSyncSettingKeys.MinConcurrentThreads,
            ExtensionSheinSyncSettingKeys.MaxConcurrentThreads);

        var lookbackDays = ExtensionSheinSyncSettingKeys.DefaultRefreshLookbackDays;
        if (values.TryGetValue(ExtensionSheinSyncSettingKeys.RefreshLookbackDays, out var lookbackRaw)
            && int.TryParse(lookbackRaw, out var lookbackParsed) && lookbackParsed > 0)
        {
            lookbackDays = lookbackParsed;
        }

        lookbackDays = Math.Clamp(
            lookbackDays,
            ExtensionSheinSyncSettingKeys.MinRefreshLookbackDays,
            ExtensionSheinSyncSettingKeys.MaxRefreshLookbackDays);

        var maxBillnos = ExtensionSheinSyncSettingKeys.DefaultRefreshMaxBillnos;
        if (values.TryGetValue(ExtensionSheinSyncSettingKeys.RefreshMaxBillnos, out var maxBillnosRaw)
            && int.TryParse(maxBillnosRaw, out var maxBillnosParsed) && maxBillnosParsed > 0)
        {
            maxBillnos = maxBillnosParsed;
        }

        maxBillnos = Math.Clamp(
            maxBillnos,
            ExtensionSheinSyncSettingKeys.MinRefreshMaxBillnos,
            ExtensionSheinSyncSettingKeys.MaxRefreshMaxBillnos);

        var captureRawPayload =
            values.TryGetValue(ExtensionSheinSyncSettingKeys.CaptureRawPayload, out var captureRaw)
            && (string.Equals(captureRaw, "true", StringComparison.OrdinalIgnoreCase) || captureRaw == "1");

        // Sync theo list SHEIN thật. refreshBillnos chỉ còn force_billnos (chẩn đoán).
        // HeaderSource cứng realtime. UpdateMode đọc từ Settings (new|all).
        _ = maxBillnos; // setting giữ tương thích admin; không còn dùng để build open-set
        var refreshBillnos = ParseSheinForceBillnos(values);

        var updateMode = ExtensionSheinSyncSettingKeys.UpdateModeNew;
        if (values.TryGetValue(ExtensionSheinSyncSettingKeys.UpdateMode, out var updateModeRaw)
            && !string.IsNullOrWhiteSpace(updateModeRaw))
        {
            var normalized = updateModeRaw.Trim().ToLowerInvariant();
            if (normalized is ExtensionSheinSyncSettingKeys.UpdateModeNew
                or ExtensionSheinSyncSettingKeys.UpdateModeAll)
            {
                updateMode = normalized;
            }
        }

        return new SheinSyncConfigDto
        {
            Enabled = enabled,
            IntervalSeconds = Math.Max(1, seconds),
            UpdateMode = updateMode,
            ConcurrentThreads = concurrentThreads,
            RefreshLookbackDays = lookbackDays,
            RefreshBillnos = refreshBillnos,
            PendingAutoMarkShipBillnos =
                await ListPendingAutoMarkShipBillnosAsync(cancellationToken),
            CaptureRawPayload = captureRawPayload,
            HeaderSource = ExtensionSheinSyncSettingKeys.HeaderSourceRealtime
        };
    }

    /// <summary>
    /// Trần số billno kẹt Auto Mark Ship trả cho extension mỗi lượt config. Backend chỉ publish
    /// <see cref="OrderAutoMarkShipPolicy.MaxAttemptsPerSyncRequest"/> lần mỗi request nên danh
    /// sách dài hơn cũng không giúp đẩy nhanh, chỉ làm phình response của một endpoint bị gọi
    /// trước mỗi lượt sync.
    /// </summary>
    private const int MaxPendingAutoMarkShipBillnos = 20;

    /// <summary>
    /// Billno mà một lượt shein-sync nữa còn đẩy tiếp được Auto Mark Ship. Luật nằm ở
    /// <see cref="OrderAutoMarkShipPolicy.PendingQueuePredicate"/> để không lệch với
    /// <see cref="OrderAutoMarkShipPolicy.ShouldAttempt"/>.
    /// </summary>
    private async Task<List<string>> ListPendingAutoMarkShipBillnosAsync(
        CancellationToken cancellationToken) =>
        await _db.SupplierPurchases
            .AsNoTracking()
            .Where(OrderAutoMarkShipPolicy.PendingQueuePredicate(DateTime.UtcNow))
            .OrderByDescending(l => l.LastSeenAt)
            .Select(l => l.SupplierOrderNo!)
            .Take(MaxPendingAutoMarkShipBillnos)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Đọc danh sách billno ép lấy lại chi tiết từ Settings (phân tách bởi dấu phẩy, xuống dòng
    /// hoặc khoảng trắng). Trả danh sách rỗng khi setting trống.
    /// </summary>
    private static List<string> ParseSheinForceBillnos(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue(ExtensionSheinSyncSettingKeys.ForceBillnos, out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([',', ';', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ExtensionSheinSyncSettingKeys.MaxForceBillnos)
            .ToList();
    }

    /// <summary>
    /// Load FulfilAccounts keyed by normalized Email and Username for SHEIN billaddr matching.
    /// </summary>
    private async Task<Dictionary<string, (Guid Id, string Name)>> BuildFulfilAccountEmailLookupAsync(
        CancellationToken cancellationToken)
    {
        var accounts = await _db.FulfilAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.Email, a.Username })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, (Guid Id, string Name)>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in accounts)
        {
            var email = (a.Email ?? string.Empty).Trim().ToLowerInvariant();
            if (email.Length > 0 && !map.ContainsKey(email))
            {
                map[email] = (a.Id, a.Name);
            }

            var username = (a.Username ?? string.Empty).Trim().ToLowerInvariant();
            if (username.Length > 0 && username.Contains('@') && !map.ContainsKey(username))
            {
                map[username] = (a.Id, a.Name);
            }
        }

        return map;
    }

    /// <summary>
    /// Resolve tài khoản mua hàng từ email trên đơn SHEIN (<c>billaddr_info.email</c>).
    /// </summary>
    private static (Guid? AccountId, string? AccountName, string? Note) ResolveFulfilAccountFromEmail(
        string? accountEmail,
        IReadOnlyDictionary<string, (Guid Id, string Name)> lookup)
    {
        var email = (accountEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0)
        {
            return (null, null, "không có billaddr email — khớp trong pool toàn bộ.");
        }

        if (lookup.TryGetValue(email, out var hit))
        {
            return (hit.Id, hit.Name, $"map FulfilAccount {hit.Name} ({email}).");
        }

        return (null, null, $"email {email} chưa map FulfilAccount — khớp trong pool toàn bộ.");
    }

    /// <summary>
    /// Dựng lại sổ cái CostUpdate của một đơn từ các lần mua của nó.
    ///
    /// Một dòng sổ cái cho mỗi lần mua được tính tiền. Mỗi lần checkout SHEIN là một lần thẻ bị trừ
    /// riêng, nên đó đúng là mức chi tiết để đối chiếu với sao kê thẻ; phí nhập tay cũng là một lần
    /// mua (nguồn <c>manual</c>) nên đi cùng đường ghi này, không còn nhánh "dòng cấp đơn" riêng.
    ///
    /// Tập được tính tiền lấy thẳng từ cờ <see cref="SupplierPurchase.IsCharged"/>, nên sổ cái và
    /// giá mua hiển thị không thể lệch nhau: hai bên đọc cùng một cột.
    /// </summary>
    /// <returns><c>true</c> when any ledger row was created, updated or removed.</returns>
    private async Task<bool> ReconcileCostUpdateTransactionsAsync(
        Order order,
        Guid actingUserId,
        Guid? paidByOverride,
        CancellationToken cancellationToken)
    {
        EnsureSupplierPurchases(order);
        var existing = await LoadCostUpdateTransactionsAsync(order.Id, cancellationToken);

        var chargeable = order.SupplierPurchases!
            .Where(p => p.IsCharged && (p.SupplierTotal.HasValue || (p.SupplierWalletPrice ?? 0m) > 0m))
            // Amount=0 chỉ hợp lệ khi breakdown đã có (subtotal đã set) — tránh dòng sổ cái
            // giả từ detail sparse / full-replace thiếu total. Ví trả hết → Total=0 nhưng Wallet > 0.
            .Where(p =>
            {
                var settled = (p.SupplierTotal ?? 0m) + (p.SupplierWalletPrice ?? 0m);
                return settled != 0m || p.SupplierSubtotal.HasValue;
            })
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .ToList();

        var currency = string.IsNullOrWhiteSpace(order.Currency) ? "USD" : order.Currency;
        var changed = false;

        foreach (var purchase in chargeable)
        {
            var accountId = purchase.FulfilAccountId ?? order.FulfilAccountId;
            var cardId = purchase.FulfilCardId
                // The order-level card belongs to the order-level account; inheriting it for
                // a billno bought from another account would charge the wrong card.
                ?? (accountId == order.FulfilAccountId ? order.FulfilCardId : null)
                ?? await ResolveActiveCardIdAsync(accountId, cancellationToken);

            // Keep the order-level card filled for the orders list and legacy filters.
            order.FulfilCardId ??= cardId;

            // Quy kết thiếu (email billing SHEIN không khớp tài khoản fulfil nào) vẫn phải ghi
            // dòng chi: tiền đã rời một cái thẻ thật, và bỏ qua ở đây từng là đường thứ hai làm
            // "đã mua mà không có trong sổ cái" — đơn hiện giá mua nhưng sổ cái trống trơn.
            // Dòng không có tài khoản/thẻ hiện trong sổ cái tổng để người đi đơn gán lại.
            var unattributed = !accountId.HasValue && !cardId.HasValue;
            var label = purchase.SupplierOrderNo
                ?? order.PlatformOrderId
                ?? order.FourSellerOrderId.ToString();

            // Số tiền sổ cái = totalPrice hóa đơn / phần thẻ (SupplierTotal) — đối chiếu sao kê thẻ.
            // Ví cashback tách cột WalletAmount; không cộng vào Amount.
            var walletAmount = purchase.SupplierWalletPrice ?? 0m;
            var cardAmount = purchase.SupplierTotal ?? 0m;
            var couponAmount = purchase.SupplierCouponPrice ?? 0m;
            var membershipAmount = purchase.SupplierMembershipDiscount ?? 0m;
            var pointsAmount = purchase.SupplierPointsDiscount ?? 0m;

            // Unique IX_FulfilTransactions_PurchaseId_CostUpdate là theo lần mua, không theo đơn.
            // Dòng chi có thể còn treo ở OrderId cũ sau khi sync gỡ purchase ngoài pool (không
            // reconcile được) — chỉ quét theo order.Id sẽ INSERT trùng và 23505 khi gắn tay.
            var row = existing.FirstOrDefault(t => t.SupplierPurchaseId == purchase.Id)
                ?? await FindCostUpdateByPurchaseIdAsync(purchase.Id, cancellationToken);
            if (row is not null && !existing.Contains(row))
            {
                existing.Add(row);
            }

            changed |= ApplyCostUpdateRow(
                row,
                order,
                supplierPurchaseId: purchase.Id,
                accountId: accountId,
                cardId: cardId,
                amount: cardAmount,
                couponAmount: couponAmount,
                walletAmount: walletAmount,
                membershipAmount: membershipAmount,
                pointsAmount: pointsAmount,
                // Mốc trừ tiền, không phải mốc tạo đơn: SHEIN cho tạo đơn trước rồi mới thu
                // tiền, nên ghi sổ theo OrderedAt sẽ lệch với bảng sao kê thẻ.
                occurredAt: purchase.SupplierPaidAt
                    ?? purchase.OrderedAt
                    ?? order.FulfilPurchasedAt
                    ?? DateTime.UtcNow,
                currency: currency,
                note: unattributed
                    ? $"Cập nhật phí đơn NCC {label} — chưa rõ tài khoản mua hàng"
                    : $"Cập nhật phí đơn NCC {label}",
                paidByUserId: purchase.PaidByUserId ?? paidByOverride ?? order.PaidByUserId,
                actingUserId: actingUserId);
        }

        // Bỏ dòng của lần mua đã gỡ khỏi đơn, mất tổng tiền, hoặc bị lần mua lại thay thế — kèm mọi
        // dòng cấp đơn còn sót từ trước khi phí nhập tay có bảng riêng.
        var chargeableIds = chargeable.Select(p => p.Id).ToHashSet();
        foreach (var row in existing)
        {
            if (row.SupplierPurchaseId.HasValue && chargeableIds.Contains(row.SupplierPurchaseId.Value))
            {
                continue;
            }

            // Lần mua đã chuyển sang đơn khác trong cùng đơn vị công việc (gắn tay / cân bằng):
            // dòng chi thuộc về đơn mới và sẽ được re-point khi reconcile đơn đó. Xoá ở đây thì
            // entity vào state Deleted, đơn mới tìm lại đúng nó nhưng không hồi sinh được, và một
            // khoản đã quẹt thẻ thật biến mất khỏi sổ cái.
            if (HasMovedToAnotherOrder(row, order.Id))
            {
                continue;
            }

            _db.FulfilTransactions.Remove(row);
            changed = true;
        }

        // Bù / đồng bộ phí xử lý BuyTrack + ThankCard khi stamp còn (sau xóa Manual trên sổ cái).
        changed |= await ReconcileOrderHandlingFeesFromStampsAsync(order, actingUserId, cancellationToken);

        return changed;
    }

    private const string BuyTrackNotePrefix = "[BuyTrack]";
    private const string ThankCardNotePrefix = "[ThankCard]";
    private const decimal DefaultBuyTrackFee = 0.5m;
    private const decimal DefaultThankCardFee = 1m;

    /// <summary>
    /// When stamp is set and amount &gt; 0, refresh fee from Settings. Stamp with amount 0
    /// (Done without fee) is left alone. When stamp clear, amounts stay 0.
    /// Also removes legacy Manual ledger rows ([BuyTrack]/[ThankCard]) — fees live on Order
    /// columns and appear as read-model fields on CostUpdate ledger rows, not as extra Debits.
    /// </summary>
    private async Task<bool> ReconcileOrderHandlingFeesFromStampsAsync(
        Order order,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        _ = actingUserId;
        var changed = false;
        // Stamp alone (Done, applyFee=false) keeps amount 0 — do not re-apply Settings fee.
        // Only refresh from Settings when fee was already charged (Amount > 0).
        // Batch-sourced price_per_label is authoritative — never overwrite from Settings.
        if (order.UspsProcessedAt.HasValue && order.UspsScanBatchId is null)
        {
            if (order.BuyTrackAmount > 0m)
            {
                var fee = await ReadHandlingFeeSettingAsync(
                    OrderHandlingSettingKeys.BuyTrack, DefaultBuyTrackFee, cancellationToken);
                if (order.BuyTrackAmount != fee)
                {
                    order.BuyTrackAmount = fee;
                    changed = true;
                }
            }
        }
        else if (!order.UspsProcessedAt.HasValue && order.BuyTrackAmount != 0m)
        {
            order.BuyTrackAmount = 0m;
            changed = true;
        }

        if (order.ThankCardProcessedAt.HasValue)
        {
            if (order.ThankCardAmount > 0m)
            {
                var fee = await ReadHandlingFeeSettingAsync(
                    OrderHandlingSettingKeys.ThankCard, DefaultThankCardFee, cancellationToken);
                if (order.ThankCardAmount != fee)
                {
                    order.ThankCardAmount = fee;
                    changed = true;
                }
            }
        }
        else if (order.ThankCardAmount != 0m)
        {
            order.ThankCardAmount = 0m;
            changed = true;
        }

        changed |= await RemoveLegacyHandlingFeeTransactionsAsync(order.Id, cancellationToken);
        return changed;
    }

    private async Task<decimal> ReadHandlingFeeSettingAsync(
        string key,
        decimal fallback,
        CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            || value < 0m)
        {
            return fallback;
        }

        return value;
    }

    /// <summary>
    /// Cleanup-only: delete legacy Manual handling-fee ledger rows for an order.
    /// Fees are stored on <see cref="Order.BuyTrackAmount"/> / <see cref="Order.ThankCardAmount"/>.
    /// </summary>
    private async Task<bool> RemoveLegacyHandlingFeeTransactionsAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.FulfilTransactions
            .Where(t =>
                t.OrderId == orderId
                && t.Source == FulfilTransaction.SourceManual
                && t.Note != null
                && (t.Note.StartsWith(BuyTrackNotePrefix) || t.Note.StartsWith(ThankCardNotePrefix)))
            .ToListAsync(cancellationToken);

        if (existing.Count == 0)
        {
            return false;
        }

        _db.FulfilTransactions.RemoveRange(existing);
        return true;
    }

    /// <summary>Create or update one CostUpdate row.</summary>
    private bool ApplyCostUpdateRow(
        FulfilTransaction? existing,
        Order order,
        Guid? supplierPurchaseId,
        Guid? accountId,
        Guid? cardId,
        decimal amount,
        decimal couponAmount,
        decimal walletAmount,
        decimal membershipAmount,
        decimal pointsAmount,
        DateTime occurredAt,
        string currency,
        string note,
        Guid? paidByUserId,
        Guid actingUserId)
    {
        if (existing is null)
        {
            var created = new FulfilTransaction
            {
                FulfilAccountId = accountId,
                FulfilCardId = cardId,
                OrderId = order.Id,
                SupplierPurchaseId = supplierPurchaseId,
                Amount = amount,
                CouponAmount = couponAmount,
                WalletAmount = walletAmount,
                MembershipAmount = membershipAmount,
                PointsAmount = pointsAmount,
                Direction = FulfilTransaction.DirectionDebit,
                Currency = currency,
                Source = FulfilTransaction.SourceCostUpdate,
                PaidByUserId = paidByUserId,
                CreatedByUserId = actingUserId,
                OccurredAt = occurredAt,
                Note = note
            };
            _db.FulfilTransactions.Add(created);
            return true;
        }

        var changed =
            existing.OrderId != order.Id
            || existing.FulfilAccountId != accountId
            || existing.FulfilCardId != cardId
            || existing.Amount != amount
            || existing.CouponAmount != couponAmount
            || existing.WalletAmount != walletAmount
            || existing.MembershipAmount != membershipAmount
            || existing.PointsAmount != pointsAmount
            // Sign guard: a CostUpdate is always a debit, and a flipped direction would
            // subtract this purchase from card spend instead of adding it.
            || !string.Equals(existing.Direction, FulfilTransaction.DirectionDebit, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Currency, currency, StringComparison.OrdinalIgnoreCase)
            || existing.PaidByUserId != paidByUserId
            || existing.OccurredAt != occurredAt
            || !string.Equals(existing.Note, note, StringComparison.Ordinal);

        if (!changed) return false;

        // Orphan / đơn cũ: chuyển dòng chi sang đơn đang reconcile thay vì INSERT (unique purchase).
        existing.OrderId = order.Id;
        existing.FulfilAccountId = accountId;
        existing.FulfilCardId = cardId;
        existing.Amount = amount;
        existing.CouponAmount = couponAmount;
        existing.WalletAmount = walletAmount;
        existing.MembershipAmount = membershipAmount;
        existing.PointsAmount = pointsAmount;
        existing.Direction = FulfilTransaction.DirectionDebit;
        existing.Currency = currency;
        existing.PaidByUserId = paidByUserId;
        existing.OccurredAt = occurredAt;
        existing.Note = note;
        existing.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// All CostUpdate rows of an order, including ones added earlier in this batch but not yet
    /// saved — sibling billnos of one Joint Shipment order are processed in the same run, and a
    /// DB-only query would miss them and insert duplicates.
    /// </summary>
    private async Task<List<FulfilTransaction>> LoadCostUpdateTransactionsAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.FulfilTransactions
            .Where(t => t.OrderId == orderId && t.Source == FulfilTransaction.SourceCostUpdate)
            .ToListAsync(cancellationToken);

        foreach (var local in _db.FulfilTransactions.Local
            .Where(t => t.OrderId == orderId && t.Source == FulfilTransaction.SourceCostUpdate))
        {
            if (_db.Entry(local).State == EntityState.Deleted) continue;
            if (!rows.Contains(local)) rows.Add(local);
        }

        return rows
            .Where(t => _db.Entry(t).State != EntityState.Deleted)
            .ToList();
    }

    /// <summary>
    /// Lần mua của dòng chi này đang thuộc một đơn <b>khác</b> đơn đang reconcile — nó chuyển chủ
    /// chứ không rời sổ cái, nên dòng chi phải sống tới lượt reconcile của đơn mới re-point nó.
    /// Chỉ soi change tracker: việc chuyển đơn luôn xảy ra trên entity đang được theo dõi, còn một
    /// truy vấn cho mỗi dòng thì vô ích mà đắt.
    /// </summary>
    private bool HasMovedToAnotherOrder(FulfilTransaction row, Guid currentOrderId)
    {
        if (row.SupplierPurchaseId is not { } purchaseId) return false;

        var purchase = _db.SupplierPurchases.Local
            .FirstOrDefault(p => p.Id == purchaseId);

        return purchase?.OrderId is { } ownerId && ownerId != currentOrderId;
    }

    /// <summary>
    /// CostUpdate theo lần mua (mọi OrderId) — để gắn tay / chuyển đơn tái sử dụng dòng orphan
    /// thay vì vi phạm <c>IX_FulfilTransactions_PurchaseId_CostUpdate</c>.
    /// </summary>
    private async Task<FulfilTransaction?> FindCostUpdateByPurchaseIdAsync(
        Guid supplierPurchaseId,
        CancellationToken cancellationToken)
    {
        foreach (var local in _db.FulfilTransactions.Local
            .Where(t =>
                t.SupplierPurchaseId == supplierPurchaseId
                && t.Source == FulfilTransaction.SourceCostUpdate))
        {
            if (_db.Entry(local).State != EntityState.Deleted)
            {
                return local;
            }
        }

        return await _db.FulfilTransactions
            .FirstOrDefaultAsync(
                t => t.SupplierPurchaseId == supplierPurchaseId
                    && t.Source == FulfilTransaction.SourceCostUpdate,
                cancellationToken);
    }

    /// <summary>Active card of a purchase account, memoized for the lifetime of this request.</summary>
    private async Task<Guid?> ResolveActiveCardIdAsync(
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        if (!accountId.HasValue) return null;
        if (_activeCardIdByAccount.TryGetValue(accountId.Value, out var cached)) return cached;

        var activeCardId = await _db.FulfilAccounts.AsNoTracking()
            .Where(a => a.Id == accountId.Value)
            .Select(a => a.ActiveCardId)
            .FirstOrDefaultAsync(cancellationToken);

        _activeCardIdByAccount[accountId.Value] = activeCardId;
        return activeCardId;
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateSettlementAsync(
        Guid orderId,
        UpdateOrderSettlementRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        await ApplySettlementPaidAsync(order, request.IsSettlementPaid, actingUserId, cancellationToken);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        var refreshed = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstAsync(o => o.Id == orderId, cancellationToken);

        refreshed.Items = order.Items;
        return MapDetail(refreshed);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateUspsProcessedAsync(
        Guid orderId,
        UpdateOrderUspsProcessedRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        _ = actingUserId;
        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        if (request.IsUspsProcessed)
        {
            order.UspsProcessedAt ??= DateTime.UtcNow;
            // Keep batch-sourced price_per_label when already pushed; Settings only for manual stamp.
            if (request.ApplyFee && order.UspsScanBatchId is null)
            {
                order.BuyTrackAmount = await ReadHandlingFeeSettingAsync(
                    OrderHandlingSettingKeys.BuyTrack, DefaultBuyTrackFee, cancellationToken);
            }
        }
        else
        {
            order.UspsProcessedAt = null;
            order.BuyTrackAmount = 0m;
            // Clear scan-push stamps so the order can be pushed again (money already spent remotely).
            order.UspsScanBatchId = null;
            order.UspsScanSentAt = null;
        }

        await RemoveLegacyHandlingFeeTransactionsAsync(order.Id, cancellationToken);

        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        var refreshed = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstAsync(o => o.Id == orderId, cancellationToken);

        refreshed.Items = order.Items;
        return MapDetail(refreshed);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateThankCardProcessedAsync(
        Guid orderId,
        UpdateOrderThankCardProcessedRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        _ = actingUserId;
        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        if (request.IsThankCardProcessed)
        {
            order.ThankCardProcessedAt ??= DateTime.UtcNow;
            if (request.ApplyFee)
            {
                order.ThankCardAmount = await ReadHandlingFeeSettingAsync(
                    OrderHandlingSettingKeys.ThankCard, DefaultThankCardFee, cancellationToken);
            }
        }
        else
        {
            order.ThankCardProcessedAt = null;
            order.ThankCardAmount = 0m;
        }

        await RemoveLegacyHandlingFeeTransactionsAsync(order.Id, cancellationToken);

        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        var refreshed = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstAsync(o => o.Id == orderId, cancellationToken);

        refreshed.Items = order.Items;
        return MapDetail(refreshed);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateRefundProcessedAsync(
        Guid orderId,
        UpdateOrderRefundProcessedRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        _ = actingUserId;
        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        if (request.IsRefundProcessed)
        {
            order.RefundProcessedAt ??= DateTime.UtcNow;
        }
        else
        {
            order.RefundProcessedAt = null;
        }

        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        var refreshed = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstAsync(o => o.Id == orderId, cancellationToken);

        refreshed.Items = order.Items;
        return MapDetail(refreshed);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateNotesAsync(
        Guid orderId,
        UpdateOrderNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var buyerMemo = NormalizeMemo(request.BuyerMemo);
        var sellerMemo = NormalizeMemo(request.SellerMemo);
        var internalMemo = NormalizeMemo(request.InternalMemo);

        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        await _fourSeller.UpdateOrderNoteAsync(
            order.OwnerId,
            new FourSellerUpdateOrderNoteDto
            {
                OrderIdList = [order.FourSellerOrderId],
                BuyerMemo = buyerMemo,
                SellerMemo = sellerMemo,
                InternalMemo = internalMemo
            },
            cancellationToken);

        var now = DateTime.UtcNow;
        order.BuyerMemo = buyerMemo;
        order.SellerMemo = sellerMemo;
        order.InternalMemo = internalMemo;
        order.UpdatedAt = now;
        order.LastSyncedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        return MapDetail(order);
    }

    /// <inheritdoc />
    public async Task<OrderDetailDto> UpdateMessageAsync(
        Guid orderId,
        UpdateOrderMessageRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        await _issueTags.SyncFromLegacyMessageAsync(
            orderId,
            request.Message,
            actingUserId,
            cancellationToken);

        var order = await _db.Orders
            .Include(o => o.Owner)
            .Include(o => o.Shop)
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .Include(o => o.IssueTags)
                .ThenInclude(t => t.IssueTag)
            .Include(o => o.FulfilAccount)
            .Include(o => o.FulfilCard)
            .Include(o => o.PaidByUser)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        await TryFillOrderItemListingOverlayAsync([order], cancellationToken);

        return MapDetail(order);
    }

    private static string? NormalizeMemo(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > 2000)
        {
            throw new BusinessException("Ghi chú tối đa 2000 ký tự.");
        }

        return trimmed;
    }

    private static string? NormalizeOrderMessage(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > 500)
        {
            throw new BusinessException("Thông báo tối đa 500 ký tự.");
        }

        return trimmed;
    }

    private static void ValidateCostField(decimal? value, string fieldName)
    {
        if (value is < 0)
        {
            throw new BusinessException($"{fieldName} phải >= 0.");
        }
    }

    private async Task<FourSellerAsyncShipOrderResultDto> PollMarkShipResultAsync(
        Guid ownerUserId,
        string requestId,
        CancellationToken cancellationToken,
        int maxAttempts = 20,
        int delayMs = 1500)
    {
        FourSellerAsyncShipOrderResultDto? last = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }

            last = await _fourSeller.GetAsyncShipOrderResultAsync(ownerUserId, requestId, cancellationToken);
            if (IsMarkShipSuccess(last) ||
                string.Equals(last.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return last;
            }
        }

        return last ?? new FourSellerAsyncShipOrderResultDto
        {
            Status = "Pending",
            Message = "Hết thời gian chờ kết quả Mark Shipped từ 4Seller."
        };
    }

    private static bool IsMarkShipSuccess(FourSellerAsyncShipOrderResultDto result) =>
        string.Equals(result.Status, "Success", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsShippingServicesCacheStaleAsync(CancellationToken cancellationToken)
    {
        if (!await _db.PlatformShippingServices.AsNoTracking().AnyAsync(cancellationToken))
        {
            return true;
        }

        var latest = await _db.PlatformShippingServices.AsNoTracking()
            .MaxAsync(s => (DateTime?)s.SyncedAt, cancellationToken);
        return latest is null || latest < DateTime.UtcNow - ShippingServicesCacheTtl;
    }

    private async Task RefreshShippingServicesCacheAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var all = await _fourSeller.ListPlatformShippingServicesAsync(ownerUserId, cancellationToken);
        var syncedAt = DateTime.UtcNow;

        await _db.PlatformShippingServices.ExecuteDeleteAsync(cancellationToken);

        foreach (var service in all)
        {
            if (string.IsNullOrWhiteSpace(service.Carrier) || service.Id <= 0)
            {
                continue;
            }

            _db.PlatformShippingServices.Add(new PlatformShippingService
            {
                FourSellerId = service.Id,
                Carrier = service.Carrier.Trim(),
                Platform = service.Platform,
                Region = service.Region,
                SyncedAt = syncedAt
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Refreshed PlatformShippingServices cache: {Count} carriers from 4Seller.",
            all.Count);
    }

    private static IReadOnlyList<FourSellerShippingServiceDto> DeduplicateCarriers(
        IReadOnlyList<FourSellerShippingServiceDto> services)
    {
        return services
            .Where(s => !string.IsNullOrWhiteSpace(s.Carrier))
            .GroupBy(s => s.Carrier.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(s => s.Carrier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<(int Inserted, int Updated)> SyncSellerAsync(
        Guid ownerId,
        IReadOnlyList<string> statuses,
        DateTime? dateFrom,
        DateTime? dateTo,
        bool openStatusesIgnoreDateFilter,
        bool skipStalePricingSweep,
        bool skipNewOrderTelegram,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var updated = 0;

        // Global shop map: 4Seller page of seller A may return orders for seller B's shops.
        var shopRows = await _db.Shops
            .AsNoTracking()
            .Select(s => new { s.Id, s.OwnerId, s.FourSellerShopId, s.Name })
            .ToListAsync(cancellationToken);
        var shopIndex = new OrderSyncShopIndex(
            shopRows.Select(s => (s.Id, s.OwnerId, s.FourSellerShopId, s.Name)));

        foreach (var status in statuses)
        {
            // Per-bucket sync (UI tab hẹp): open buckets ignore create-date lookback so older
            // to_ship/on_hold still refresh; shipped/canceled stay date-bounded.
            // Bucket "all" trộn cả hai nhóm nên không thể bỏ lọc ngày — bỏ sẽ kéo toàn bộ lịch sử.
            var statusDateFrom = dateFrom;
            var statusDateTo = dateTo;
            if (openStatusesIgnoreDateFilter
                && dateFrom.HasValue
                && dateTo.HasValue
                && !string.Equals(status, AllStatusBucket, StringComparison.OrdinalIgnoreCase)
                && !DateFilteredStatuses.Contains(status))
            {
                statusDateFrom = null;
                statusDateTo = null;
            }

            var pageCurrent = 1;
            const int pageSize = 100;
            int total;

            do
            {
                var page = await _fourSeller.GetOrderPageAsync(
                    ownerId,
                    new FourSellerOrderPageRequest
                    {
                        PageCurrent = pageCurrent,
                        PageSize = pageSize,
                        OrderStatus = status,
                        DateFrom = statusDateFrom,
                        DateTo = statusDateTo
                    },
                    cancellationToken);

                total = page.Total;
                var (pageInserted, pageUpdated) = await UpsertRecordsAsync(
                    ownerId,
                    page.Records,
                    shopIndex,
                    skipNewOrderTelegram,
                    cancellationToken);
                inserted += pageInserted;
                updated += pageUpdated;

                pageCurrent++;
            } while ((pageCurrent - 1) * pageSize < total
                     && pageCurrent <= MaxPagesPerQuery);

            if (pageCurrent > MaxPagesPerQuery && (pageCurrent - 1) * pageSize < total)
            {
                _logger.LogWarning(
                    "Order sync hit page cap {MaxPages} for owner {OwnerId} status {Status} (total={Total}); remaining pages deferred to next run",
                    MaxPagesPerQuery,
                    ownerId,
                    status,
                    total);
            }
        }

        // Orders outside the shipped/canceled lookback never appear in touched pages, so
        // empty-pricing retries must run against the owner's leftover rows too — unless the
        // caller is the status loop, which only refreshes platformOrderStatus.
        if (!skipStalePricingSweep)
        {
            await EnrichStalePricingForOwnerAsync(ownerId, cancellationToken);
        }

        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertRecordsAsync(
        Guid ownerId,
        IReadOnlyList<FourSellerOrderRecordDto> records,
        OrderSyncShopIndex shopIndex,
        bool skipNewOrderTelegram,
        CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return (0, 0);
        }

        // Bound change-tracking to a single page: disable auto-detect (we DetectChanges manually),
        // and Clear() afterwards so a long run does not accumulate every loaded order in memory.
        var originalAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return await UpsertPageCoreAsync(
                ownerId,
                records,
                shopIndex,
                skipNewOrderTelegram,
                cancellationToken);
        }
        finally
        {
            _db.ChangeTracker.Clear();
            _db.ChangeTracker.AutoDetectChangesEnabled = originalAutoDetect;
        }
    }

    private async Task<(int Inserted, int Updated)> UpsertPageCoreAsync(
        Guid ownerId,
        IReadOnlyList<FourSellerOrderRecordDto> records,
        OrderSyncShopIndex shopIndex,
        bool skipNewOrderTelegram,
        CancellationToken cancellationToken)
    {
        // One record per 4Seller order id — last wins within the page.
        var uniqueRecords = records
            .Where(r => r.OrderId > 0)
            .GroupBy(r => r.OrderId)
            .Select(g => g.Last())
            .ToList();

        var orderIds = uniqueRecords.Select(r => r.OrderId).ToList();
        var myShopIds = shopIndex.GetShopIdsForOwner(ownerId);
        var myFsIds = shopIndex.GetFourSellerShopIdsForOwner(ownerId);

        if (orderIds.Count > 0)
        {
            var foreignOrders = await _db.Orders
                .Where(o => orderIds.Contains(o.FourSellerOrderId) && o.OwnerId != ownerId)
                .Select(o => new
                {
                    o.Id,
                    o.FourSellerOrderId,
                    o.ShopId,
                    o.FourSellerShopId,
                    o.PlatformOrderId,
                    o.OwnerId
                })
                .ToListAsync(cancellationToken);

            if (foreignOrders.Count > 0)
            {
                var reclaimable = foreignOrders
                    .Where(o =>
                        (o.ShopId.HasValue && myShopIds.Contains(o.ShopId.Value))
                        || (o.FourSellerShopId is > 0 && myFsIds.Contains(o.FourSellerShopId.Value)))
                    .ToList();

                var handledFsIds = new HashSet<long>();

                if (reclaimable.Count > 0)
                {
                    var reclaimPlatformIds = reclaimable
                        .Where(o => !string.IsNullOrWhiteSpace(o.PlatformOrderId))
                        .Select(o => o.PlatformOrderId!)
                        .Distinct()
                        .ToList();
                    var localByPlatform = reclaimPlatformIds.Count == 0
                        ? new Dictionary<string, Guid>(StringComparer.Ordinal)
                        : (await _db.Orders
                            .AsNoTracking()
                            .Where(o => o.OwnerId == ownerId
                                        && o.PlatformOrderId != null
                                        && reclaimPlatformIds.Contains(o.PlatformOrderId))
                            .Select(o => new { o.Id, PlatformOrderId = o.PlatformOrderId! })
                            .ToListAsync(cancellationToken))
                            .GroupBy(o => o.PlatformOrderId, StringComparer.Ordinal)
                            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

                    var simpleReclaimIds = new List<Guid>();
                    foreach (var foreign in reclaimable)
                    {
                        Guid? matchedShopId = null;
                        if (foreign.ShopId.HasValue && myShopIds.Contains(foreign.ShopId.Value))
                        {
                            matchedShopId = foreign.ShopId;
                        }
                        else if (foreign.FourSellerShopId is > 0)
                        {
                            matchedShopId = shopIndex.ResolveByFourSellerShopId(foreign.FourSellerShopId)?.ShopId;
                        }

                        if (!string.IsNullOrWhiteSpace(foreign.PlatformOrderId)
                            && localByPlatform.TryGetValue(foreign.PlatformOrderId, out var localId))
                        {
                            var pair = await _db.Orders
                                .Include(o => o.Items)
                                .Where(o => o.Id == localId || o.Id == foreign.Id)
                                .ToListAsync(cancellationToken);
                            if (pair.Count == 2)
                            {
                                var keep = OrderOwnershipMerge.ChooseKeep(pair[0], pair[1], ownerId);
                                var drop = keep.Id == pair[0].Id ? pair[1] : pair[0];
                                await OrderOwnershipMerge.MergeIntoAsync(
                                    _db,
                                    keep,
                                    drop,
                                    ownerId,
                                    matchedShopId,
                                    preferredFourSellerOrderId: foreign.FourSellerOrderId,
                                    _logger,
                                    cancellationToken);
                                handledFsIds.Add(foreign.FourSellerOrderId);
                                continue;
                            }
                        }

                        simpleReclaimIds.Add(foreign.Id);
                    }

                    if (simpleReclaimIds.Count > 0)
                    {
                        var reclaimNow = DateTime.UtcNow;
                        var toReclaim = await _db.Orders
                            .Where(o => simpleReclaimIds.Contains(o.Id))
                            .ToListAsync(cancellationToken);
                        foreach (var order in toReclaim)
                        {
                            order.OwnerId = ownerId;
                            if (order.FourSellerShopId is > 0)
                            {
                                var matched = shopIndex.ResolveByFourSellerShopId(order.FourSellerShopId);
                                if (matched is not null && matched.OwnerId == ownerId)
                                {
                                    order.ShopId = matched.ShopId;
                                }
                            }

                            order.UpdatedAt = reclaimNow;
                            handledFsIds.Add(order.FourSellerOrderId);
                        }

                        _logger.LogInformation(
                            "Reclaimed {Count} orders onto seller {OwnerId} after shop transfer",
                            toReclaim.Count,
                            ownerId);
                    }

                    if (handledFsIds.Count > 0)
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                    }

                    foreignOrders = foreignOrders
                        .Where(o => !handledFsIds.Contains(o.FourSellerOrderId))
                        .ToList();
                }

                if (foreignOrders.Count > 0)
                {
                    var skip = foreignOrders.Select(o => o.FourSellerOrderId).ToHashSet();
                    foreach (var foreign in foreignOrders)
                    {
                        _logger.LogInformation(
                            "Skipping 4Seller order {FourSellerOrderId} owned by seller {TrueOwnerId} during sync of {OwnerId}",
                            foreign.FourSellerOrderId,
                            foreign.OwnerId,
                            ownerId);
                    }

                    uniqueRecords = uniqueRecords.Where(r => !skip.Contains(r.OrderId)).ToList();
                    orderIds = uniqueRecords.Select(r => r.OrderId).ToList();
                }
            }
        }

        if (uniqueRecords.Count == 0)
        {
            return (0, 0);
        }

        var platformOrderIds = uniqueRecords
            .Select(r => NormalizePlatformOrderId(FirstNonEmpty(r.PlatformOrderId, r.PlatformOrderNo)))
            .Where(id => id is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Before upsert: pull in foreign rows that share PlatformOrderId on shops we own
        // (different FourSellerOrderId) so we never insert a second local copy.
        if (platformOrderIds.Count > 0 && (myShopIds.Count > 0 || myFsIds.Count > 0))
        {
            var foreignByPlatform = await _db.Orders
                .Include(o => o.Items)
                .Where(o => o.OwnerId != ownerId
                            && o.PlatformOrderId != null
                            && platformOrderIds.Contains(o.PlatformOrderId)
                            && ((o.ShopId != null && myShopIds.Contains(o.ShopId.Value))
                                || (o.FourSellerShopId != null
                                    && o.FourSellerShopId > 0
                                    && myFsIds.Contains(o.FourSellerShopId.Value))))
                .ToListAsync(cancellationToken);

            if (foreignByPlatform.Count > 0)
            {
                var platformTouched = false;
                foreach (var foreign in foreignByPlatform)
                {
                    var platformKey = NormalizePlatformOrderId(foreign.PlatformOrderId);
                    if (platformKey is null)
                    {
                        continue;
                    }

                    var local = await _db.Orders
                        .Include(o => o.Items)
                        .FirstOrDefaultAsync(
                            o => o.OwnerId == ownerId && o.PlatformOrderId == platformKey,
                            cancellationToken);

                    Guid? matchedShopId = foreign.ShopId;
                    if (matchedShopId is null && foreign.FourSellerShopId is > 0)
                    {
                        var matched = shopIndex.ResolveByFourSellerShopId(foreign.FourSellerShopId);
                        if (matched is not null && matched.OwnerId == ownerId)
                        {
                            matchedShopId = matched.ShopId;
                        }
                    }

                    var pageFsId = uniqueRecords
                        .Where(r => string.Equals(
                            NormalizePlatformOrderId(FirstNonEmpty(r.PlatformOrderId, r.PlatformOrderNo)),
                            platformKey,
                            StringComparison.Ordinal))
                        .Select(r => (long?)r.OrderId)
                        .FirstOrDefault();

                    if (local is not null)
                    {
                        var keep = OrderOwnershipMerge.ChooseKeep(local, foreign, ownerId);
                        var drop = keep.Id == local.Id ? foreign : local;
                        await OrderOwnershipMerge.MergeIntoAsync(
                            _db,
                            keep,
                            drop,
                            ownerId,
                            matchedShopId,
                            preferredFourSellerOrderId: pageFsId,
                            _logger,
                            cancellationToken);
                    }
                    else
                    {
                        foreign.OwnerId = ownerId;
                        if (matchedShopId.HasValue)
                        {
                            foreign.ShopId = matchedShopId;
                        }

                        if (pageFsId is > 0)
                        {
                            var fsConflict = await _db.Orders
                                .AsNoTracking()
                                .AnyAsync(
                                    o => o.FourSellerOrderId == pageFsId.Value && o.Id != foreign.Id,
                                    cancellationToken);
                            if (!fsConflict)
                            {
                                foreign.FourSellerOrderId = pageFsId.Value;
                            }
                        }

                        foreign.UpdatedAt = DateTime.UtcNow;
                    }

                    platformTouched = true;
                }

                if (platformTouched)
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        var existingByFourSellerId = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.SupplierPurchases)
            .AsSplitQuery()
            .Where(o => o.OwnerId == ownerId && orderIds.Contains(o.FourSellerOrderId))
            .ToDictionaryAsync(o => o.FourSellerOrderId, cancellationToken);

        // Also load by business key (owner + platform order code) so reassigned 4Seller ids update
        // instead of inserting duplicates.
        var existingByPlatformOrderId = new Dictionary<string, Order>(StringComparer.Ordinal);
        if (platformOrderIds.Count > 0)
        {
            var byPlatform = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.SupplierPurchases)
                .AsSplitQuery()
                .Where(o =>
                    o.OwnerId == ownerId &&
                    o.PlatformOrderId != null &&
                    platformOrderIds.Contains(o.PlatformOrderId))
                .ToListAsync(cancellationToken);

            foreach (var group in byPlatform.GroupBy(
                         o => NormalizePlatformOrderId(o.PlatformOrderId),
                         StringComparer.Ordinal))
            {
                if (group.Key is null)
                {
                    continue;
                }

                var ordered = group
                    .OrderByDescending(o => o.LastSyncedAt)
                    .ThenByDescending(o => o.UpdatedAt ?? o.CreatedAt)
                    .ToList();
                var keep = ordered[0];
                existingByPlatformOrderId[group.Key] = keep;
                existingByFourSellerId.TryAdd(keep.FourSellerOrderId, keep);

                foreach (var duplicate in ordered.Skip(1))
                {
                    _logger.LogWarning(
                        "Removing duplicate order PlatformOrderId={PlatformOrderId} FourSellerOrderId={FourSellerOrderId} Id={Id}",
                        group.Key,
                        duplicate.FourSellerOrderId,
                        duplicate.Id);
                    existingByFourSellerId.Remove(duplicate.FourSellerOrderId);
                    RemoveOrderEntity(duplicate);
                }
            }
        }

        var now = DateTime.UtcNow;
        var newlyInserted = new List<Order>();
        var newlyCanceled = new List<Order>();
        var touchedOrders = new List<Order>();
        var pricingEnrichOrders = new List<Order>();
        var newOrders = new List<Order>();
        var adoptedForeignOrders = new List<Order>();
        // Existing orders touched this page + whether SyncItems added/removed line items
        // (structural changes are invisible via ChangeTracker once removed from the collection).
        // RawJson kept so we only refresh RawPayload after a real business-field change.
        var existingOrders = new List<(Order Order, bool StructuralItemChange, string? RawJson)>();
        var shopNameFallbacks = new Dictionary<Guid, string?>();

        foreach (var record in uniqueRecords)
        {
            var link = shopIndex.Resolve(record.ShopId, record.ShopName, ownerId);
            var resolvedOwnerId = link?.OwnerId ?? ownerId;
            var localShopId = link?.ShopId;
            var platformOrderId = NormalizePlatformOrderId(
                FirstNonEmpty(record.PlatformOrderId, record.PlatformOrderNo));

            // Page of seller A returned an order for seller B's shop.
            if (resolvedOwnerId != ownerId)
            {
                var existingUnderSyncOwner = ResolveExistingOrder(
                    record.OrderId,
                    platformOrderId,
                    existingByFourSellerId,
                    existingByPlatformOrderId);
                if (existingUnderSyncOwner is not null)
                {
                    // Leave mis-attributed rows for reclaim on the true owner's sync.
                    continue;
                }

                _logger.LogWarning(
                    "4Seller page of seller {SyncOwnerId} returned order {PlatformOrderId} of shop {FourSellerShopId} owned by {TrueOwnerId}",
                    ownerId,
                    platformOrderId ?? record.OrderId.ToString(),
                    record.ShopId,
                    resolvedOwnerId);

                var order = new Order
                {
                    OwnerId = resolvedOwnerId,
                    FourSellerOrderId = record.OrderId,
                    CreatedAt = now
                };
                ApplyRecord(order, record, resolvedOwnerId, localShopId, now, assignRawPayload: true);
                SyncItems(order, record.OrderItemInfoList, now);
                _db.Orders.Add(order);
                existingByFourSellerId[record.OrderId] = order;
                if (platformOrderId is not null)
                {
                    existingByPlatformOrderId[platformOrderId] = order;
                }

                if (IsCanceledStatus(order.PlatformOrderStatus, order.OrderStatus))
                {
                    newlyCanceled.Add(order);
                }
                else
                {
                    newlyInserted.Add(order);
                }

                // Do not pricing-enrich with the syncing seller's cookie — true owner will enrich.
                newOrders.Add(order);
                adoptedForeignOrders.Add(order);
                shopNameFallbacks[order.Id] = FirstNonEmpty(record.ShopName, link?.Name);
                continue;
            }

            var existing = ResolveExistingOrder(
                record.OrderId,
                platformOrderId,
                existingByFourSellerId,
                existingByPlatformOrderId);

            if (existing is not null)
            {
                existing = RemapFourSellerOrderId(existing, record.OrderId, existingByFourSellerId);
                var wasCanceled = IsCanceledStatus(existing.PlatformOrderStatus, existing.OrderStatus);
                // Existing: skip RawPayload so volatile 4Seller JSON cannot dirty every row.
                LogClearedMilestones(
                    existing,
                    ApplyRecord(existing, record, ownerId, localShopId, now, assignRawPayload: false));
                var structuralItemChange = SyncItems(existing, record.OrderItemInfoList, now);
                if (!wasCanceled && IsCanceledStatus(existing.PlatformOrderStatus, existing.OrderStatus))
                {
                    newlyCanceled.Add(existing);
                    shopNameFallbacks[existing.Id] = record.ShopName;
                }

                if (platformOrderId is not null)
                {
                    existingByPlatformOrderId[platformOrderId] = existing;
                }

                touchedOrders.Add(existing);
                pricingEnrichOrders.Add(existing);
                existingOrders.Add((existing, structuralItemChange, record.RawJson));
            }
            else
            {
                var order = new Order
                {
                    OwnerId = ownerId,
                    FourSellerOrderId = record.OrderId,
                    CreatedAt = now
                };
                ApplyRecord(order, record, ownerId, localShopId, now, assignRawPayload: true);
                SyncItems(order, record.OrderItemInfoList, now);
                _db.Orders.Add(order);
                existingByFourSellerId[record.OrderId] = order;
                if (platformOrderId is not null)
                {
                    existingByPlatformOrderId[platformOrderId] = order;
                }

                if (IsCanceledStatus(order.PlatformOrderStatus, order.OrderStatus))
                {
                    newlyCanceled.Add(order);
                }
                else
                {
                    newlyInserted.Add(order);
                }

                touchedOrders.Add(order);
                pricingEnrichOrders.Add(order);
                newOrders.Add(order);
                shopNameFallbacks[order.Id] = record.ShopName;
            }
        }

        await TryFillOrderItemSourceUrlsFromListingsAsync(touchedOrders.Concat(adoptedForeignOrders).ToList(), cancellationToken);
        await TryFillOrderItemProductFieldsFromListingsAsync(touchedOrders.Concat(adoptedForeignOrders).ToList(), cancellationToken);

        // Detect all business changes made above (auto-detect is disabled for this page).
        _db.ChangeTracker.DetectChanges();

        // New orders always get timestamps. Existing orders only when something actually changed,
        // so unchanged orders produce no UPDATE (the dominant cost on frequent, growing syncs).
        var inserted = newOrders.Count;
        foreach (var order in newOrders)
        {
            StampOrderTimestamps(order, now, stampAllItems: true);
        }

        var updated = 0;
        foreach (var (order, structuralItemChange, rawJson) in existingOrders)
        {
            var orderChanged = _db.Entry(order).State == EntityState.Modified;
            var itemsChanged = structuralItemChange || order.Items.Any(i =>
            {
                var state = _db.Entry(i).State;
                return state == EntityState.Added || state == EntityState.Modified;
            });

            if (orderChanged || itemsChanged)
            {
                // Refresh raw snapshot only when business fields actually changed.
                if (!string.IsNullOrEmpty(rawJson))
                {
                    order.RawPayload = rawJson;
                }

                StampOrderTimestamps(order, now, stampAllItems: false);
                updated++;
            }
        }

        // Second pass so the timestamp stamps above are persisted (auto-detect is disabled).
        _db.ChangeTracker.DetectChanges();

        bool saved;
        try
        {
            saved = await SaveOrderPageChangesAsync(ownerId, cancellationToken);
        }
        catch (DbUpdateException ex) when (
            adoptedForeignOrders.Count > 0 && IsFourSellerOrderIdConflict(ex))
        {
            // True owner inserted the same FourSellerOrderId in parallel.
            if (!await TryRecoverAdoptedUniqueConflictAsync(
                    adoptedForeignOrders, newlyInserted, newlyCanceled, newOrders, cancellationToken))
            {
                return (0, updated);
            }

            inserted = newOrders.Count;
            saved = true;
        }

        if (!saved)
        {
            return (inserted, updated);
        }

        await EnrichPricingDetailsAsync(ownerId, pricingEnrichOrders, cancellationToken);

        // Notify only after product line items are persisted/enriched (complete Title+SKU).
        // Status loop skips this — new-order Telegram belongs to the light sync (+ pending sweep).
        if (OrderSyncTelegramPolicy.ShouldNotifyNewOrders(skipNewOrderTelegram, newlyInserted.Count))
        {
            // Notifier claims TelegramNewOrderNotifiedAt before send (ExecuteUpdate).
            await _telegram.NotifyNewOrdersAsync(newlyInserted, shopNameFallbacks, cancellationToken);
        }

        if (newlyCanceled.Count > 0)
        {
            await _telegram.NotifyCanceledOrdersAsync(newlyCanceled, shopNameFallbacks, cancellationToken);
        }

        return (inserted, updated);
    }

    /// <summary>
    /// When seller A adopts an order that seller B inserted in parallel, Postgres unique on
    /// <c>FourSellerOrderId</c> fails — detach the adopted rows and continue the page without them.
    /// </summary>
    private async Task<bool> TryRecoverAdoptedUniqueConflictAsync(
        List<Order> adoptedForeignOrders,
        List<Order> newlyInserted,
        List<Order> newlyCanceled,
        List<Order> newOrders,
        CancellationToken cancellationToken)
    {
        foreach (var order in adoptedForeignOrders)
        {
            newlyInserted.Remove(order);
            newlyCanceled.Remove(order);
            newOrders.Remove(order);
            var entry = _db.Entry(order);
            if (entry.State != EntityState.Detached)
            {
                foreach (var item in order.Items.ToList())
                {
                    _db.Entry(item).State = EntityState.Detached;
                }

                entry.State = EntityState.Detached;
            }
        }

        adoptedForeignOrders.Clear();

        try
        {
            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Dropped adopted foreign-shop inserts after FourSellerOrderId unique conflict (true owner likely won the race)");
            return true;
        }
        catch (DbUpdateException ex) when (IsFourSellerOrderIdConflict(ex))
        {
            _logger.LogWarning(ex, "Order page save still conflicting after dropping adopted inserts");
            return false;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static bool IsFourSellerOrderIdConflict(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_Orders_FourSellerOrderId", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("FourSellerOrderId", StringComparison.OrdinalIgnoreCase)
                && message.Contains("23505", StringComparison.Ordinal));
    }

    /// <summary>
    /// Calls 4Seller order detail for orders still missing a real TikTok payout breakdown.
    /// Empty all-zero detail (some refunds) is not stamped so the next sync retries.
    /// Cookie/proxy is validated once; HTTP fan-out is limited; EF apply + save stays on the sync thread.
    /// </summary>
    private async Task EnrichPricingDetailsAsync(
        Guid ownerId,
        IReadOnlyList<Order> touchedOrders,
        CancellationToken cancellationToken)
    {
        var needsEnrich = touchedOrders
            .Where(o => o.OwnerId == ownerId)
            .Where(NeedsPricingEnrich)
            .GroupBy(o => o.FourSellerOrderId)
            .Select(g => g.First())
            .ToList();
        if (needsEnrich.Count == 0)
        {
            return;
        }

        // Prefer newest paid/create first so open revenue is correct sooner during backfill.
        var batch = needsEnrich
            .OrderByDescending(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
            .Take(PricingDetailMaxPerPage)
            .ToList();

        IReadOnlyDictionary<long, FourSellerOrderAmountDetailDto> fetched;
        try
        {
            fetched = await _fourSeller.GetOrderDetailsAsync(
                ownerId,
                batch.Select(o => o.FourSellerOrderId).ToList(),
                PricingDetailConcurrency,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never fail the whole page upsert / Telegram path because of pricing enrich.
            _logger.LogWarning(
                ex,
                "Pricing detail batch failed for owner {OwnerId}; will retry next sync",
                ownerId);
            return;
        }

        if (fetched.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var enriched = 0;
        var skippedEmpty = 0;
        foreach (var order in batch)
        {
            if (!fetched.TryGetValue(order.FourSellerOrderId, out var detail))
            {
                continue;
            }

            if (ApplyPricingDetail(order, detail, now))
            {
                enriched++;
            }
            else
            {
                skippedEmpty++;
            }
        }

        if (skippedEmpty > 0)
        {
            _logger.LogInformation(
                "Skipped empty 4Seller pricing for {Skipped} orders (owner {OwnerId}); will retry",
                skippedEmpty,
                ownerId);
        }

        if (enriched == 0)
        {
            return;
        }

        _db.ChangeTracker.DetectChanges();
        await SaveOrderPageChangesAsync(ownerId, cancellationToken);
        _logger.LogInformation(
            "Enriched TikTok pricing for {Count}/{Batch} (pending {Pending}) orders (owner {OwnerId})",
            enriched,
            batch.Count,
            needsEnrich.Count - batch.Count,
            ownerId);
    }

    /// <summary>
    /// Picks up paid orders whose list/detail amounts are still all zero — they drop out of the
    /// shipped lookback after a week and would otherwise never be retried.
    /// </summary>
    private async Task EnrichStalePricingForOwnerAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var stale = await _db.Orders
            .Where(o =>
                o.OwnerId == ownerId
                && o.FourSellerOrderId > 0
                && o.IsCustomerPaid
                && o.Amount == 0
                && o.TotalPaidAmount == 0
                && (o.SellerNetAmount == null || o.SellerNetAmount == 0)
                && (o.ProductAmountBeforeDiscount == null || o.ProductAmountBeforeDiscount == 0))
            .OrderByDescending(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
            .Take(PricingDetailMaxPerPage)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        await EnrichPricingDetailsAsync(ownerId, stale, cancellationToken);
    }

    private static bool NeedsPricingEnrich(Order order) =>
        order.FourSellerOrderId > 0
        && (
            (order.PricingDetailSyncedAt == null && !order.SellerNetAmount.HasValue)
            || (
                order.IsCustomerPaid
                && order.Amount == 0
                && order.TotalPaidAmount == 0
                && (order.SellerNetAmount ?? 0) == 0
                && (order.ProductAmountBeforeDiscount ?? 0) == 0));

    /// <summary>
    /// Persists the current page. A concurrent SHEIN/order write that deletes a tracked row
    /// must not abort the rest of this seller's buckets. Missing OrderItems that we still
    /// intend to write are re-inserted (4Seller is source of truth); other missing rows
    /// are detached so a duplicate-merge delete is not undone.
    /// </summary>
    private async Task<bool> SaveOrderPageChangesAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    "Order sync concurrency owner {OwnerId} attempt {Attempt}/{Max}: {Entities}",
                    ownerId,
                    attempt,
                    maxAttempts,
                    string.Join(
                        ", ",
                        ex.Entries.Select(e => $"{e.Metadata.ClrType.Name}:{e.State}")));

                foreach (var entry in ex.Entries)
                {
                    await ResolveOrderSyncConcurrencyEntryAsync(entry, cancellationToken);
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Order sync concurrency retry failed owner {OwnerId}", ownerId);
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                }

                return false;
            }
        }
    }

    /// <summary>
    /// Parent order still exists and no other line already occupies
    /// <c>(OrderId, FourSellerOrderItemId)</c> — otherwise INSERT would hit FK / unique.
    /// </summary>
    private async Task<bool> CanReinsertOrderItemAsync(OrderItem item, CancellationToken cancellationToken)
    {
        var parentExists = await _db.Orders
            .AsNoTracking()
            .AnyAsync(o => o.Id == item.OrderId, cancellationToken);
        if (!parentExists)
        {
            return false;
        }

        var duplicateKey = await _db.OrderItems
            .AsNoTracking()
            .AnyAsync(
                i => i.OrderId == item.OrderId
                     && i.FourSellerOrderItemId == item.FourSellerOrderItemId,
                cancellationToken);
        return !duplicateKey;
    }

    private async Task ResolveOrderSyncConcurrencyEntryAsync(
        EntityEntry entry,
        CancellationToken cancellationToken)
    {
        var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
        var action = OrderSyncConcurrency.Resolve(
            entry.Metadata.ClrType,
            entry.State,
            databaseRowExists: databaseValues is not null);

        switch (action)
        {
            case OrderSyncConcurrencyAction.Reinsert:
                if (entry.Entity is OrderItem item
                    && !await CanReinsertOrderItemAsync(item, cancellationToken))
                {
                    entry.State = EntityState.Detached;
                    break;
                }

                entry.State = EntityState.Added;
                break;
            case OrderSyncConcurrencyAction.RefreshOriginals:
                entry.OriginalValues.SetValues(databaseValues!);
                break;
            default:
                entry.State = EntityState.Detached;
                break;
        }
    }

    private static bool ApplyPricingDetail(
        Order order,
        FourSellerOrderAmountDetailDto detail,
        DateTime nowUtc)
    {
        if (!TikTokSellerNetCalculator.HasMeaningfulBreakdown(
                detail.ProductAmountBeforeDiscount,
                detail.TotalPaidAmount,
                detail.ShippingFeeBeforeDiscount,
                detail.ProductSellerDiscount))
        {
            return false;
        }

        order.ProductAmountBeforeDiscount = detail.ProductAmountBeforeDiscount;
        order.ProductPlatformDiscount = detail.ProductPlatformDiscount;
        order.ProductSellerDiscount = detail.ProductSellerDiscount;
        order.ShippingFeeBeforeDiscount = detail.ShippingFeeBeforeDiscount;
        order.ShippingFeeSellerDiscount = detail.ShippingFeeSellerDiscount;
        order.PlatformTotalTax = detail.TotalTax;
        order.PlatformOtherFee = detail.OtherFee;

        // Prefer detail shippingMethod for net formula (list payload can be thinner).
        if (!string.IsNullOrWhiteSpace(detail.ShippingMethod))
        {
            order.ShippingMethod = detail.ShippingMethod.Trim();
        }

        if (detail.TotalPaidAmount.HasValue)
        {
            order.TotalPaidAmount = TikTokSellerNetCalculator.KeepLastNonZero(
                order.TotalPaidAmount, detail.TotalPaidAmount.Value);
        }

        order.SellerNetAmount = TikTokSellerNetCalculator.Compute(
            detail.ProductAmountBeforeDiscount,
            detail.ProductSellerDiscount,
            detail.ProductPlatformDiscount,
            detail.ShippingFeeBeforeDiscount,
            detail.ShippingFeeSellerDiscount,
            detail.TotalTax,
            detail.OtherFee,
            order.ShippingMethod);
        order.PricingDetailSyncedAt = nowUtc;
        order.UpdatedAt = nowUtc;
        return true;
    }

    /// <summary>
    /// Stamps <see cref="Order.UpdatedAt"/> / <see cref="Order.LastSyncedAt"/> and item
    /// <see cref="OrderItem.UpdatedAt"/>. For existing orders only the new/modified items are
    /// stamped (via change-tracker state) so untouched rows stay clean.
    /// </summary>
    private void StampOrderTimestamps(Order order, DateTime now, bool stampAllItems)
    {
        order.UpdatedAt = now;
        order.LastSyncedAt = now;

        foreach (var item in order.Items)
        {
            if (stampAllItems)
            {
                item.UpdatedAt = now;
                continue;
            }

            var state = _db.Entry(item).State;
            if (state == EntityState.Added || state == EntityState.Modified)
            {
                item.UpdatedAt = now;
            }
        }
    }

    /// <summary>
    /// Fills missing <see cref="OrderItem.SourceUrl"/> from <see cref="Listing.SourceUrl"/>
    /// matched by order owner + line-item <c>SellerSku</c>, then by <c>Order.ShopId</c> (shop-transfer safe).
    /// Used on sync (persist) and list/detail (in-memory overlay when AsNoTracking).
    /// </summary>
    private async Task TryFillOrderItemSourceUrlsFromListingsAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var needing = orders
            .SelectMany(o => (o.Items ?? []).Select(i => (Order: o, Item: i)))
            .Where(x =>
                string.IsNullOrWhiteSpace(x.Item.SourceUrl)
                && !string.IsNullOrWhiteSpace(x.Item.SellerSku))
            .ToList();
        if (needing.Count == 0)
        {
            return;
        }

        var ownerIds = needing.Select(x => x.Order.OwnerId).Distinct().ToList();
        var shopIds = needing
            .Where(x => x.Order.ShopId.HasValue)
            .Select(x => x.Order.ShopId!.Value)
            .Distinct()
            .ToList();
        var skuKeys = needing
            .Select(x => x.Item.SellerSku!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var skuLower = skuKeys.Select(s => s.ToLowerInvariant()).ToList();

        var candidates = await (
                from v in _db.ListingVariations.AsNoTracking()
                join l in _db.Listings.AsNoTracking() on v.ListingId equals l.Id
                where l.SourceUrl != null
                      && l.SourceUrl != ""
                      && v.SellerSku != null
                      && skuLower.Contains(v.SellerSku.ToLower())
                      && (ownerIds.Contains(l.OwnerId)
                          || (l.ShopId != null && shopIds.Contains(l.ShopId.Value)))
                select new { l.OwnerId, l.ShopId, v.SellerSku, l.SourceUrl, l.UpdatedAt })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var sourceByOwnerSku = new Dictionary<(Guid OwnerId, string SkuLower), string>();
        var sourceByShopSku = new Dictionary<(Guid ShopId, string SkuLower), string>();
        foreach (var row in candidates.OrderByDescending(r => r.UpdatedAt))
        {
            var sku = row.SellerSku?.Trim();
            if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(row.SourceUrl))
            {
                continue;
            }

            var skuKey = sku.ToLowerInvariant();
            var url = row.SourceUrl.Trim();
            sourceByOwnerSku.TryAdd((row.OwnerId, skuKey), url);
            if (row.ShopId is Guid shopId)
            {
                sourceByShopSku.TryAdd((shopId, skuKey), url);
            }
        }

        foreach (var (order, item) in needing)
        {
            var sku = item.SellerSku!.Trim().ToLowerInvariant();
            if (sourceByOwnerSku.TryGetValue((order.OwnerId, sku), out var sourceUrl)
                || (order.ShopId is Guid sid && sourceByShopSku.TryGetValue((sid, sku), out sourceUrl)))
            {
                item.SourceUrl = sourceUrl;
            }
        }
    }

    /// <summary>
    /// Fills missing <see cref="OrderItem.Title"/> / <see cref="OrderItem.ImgUrl"/> from listing
    /// variation (OwnerId + SellerSku) so Telegram notify has complete product fields.
    /// </summary>
    private async Task TryFillOrderItemProductFieldsFromListingsAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var needing = orders
            .SelectMany(o => (o.Items ?? []).Select(i => (Order: o, Item: i)))
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Item.SellerSku)
                && (string.IsNullOrWhiteSpace(x.Item.Title) || string.IsNullOrWhiteSpace(x.Item.ImgUrl)))
            .ToList();
        if (needing.Count == 0)
        {
            return;
        }

        var ownerIds = needing.Select(x => x.Order.OwnerId).Distinct().ToList();
        var shopIds = needing
            .Where(x => x.Order.ShopId.HasValue)
            .Select(x => x.Order.ShopId!.Value)
            .Distinct()
            .ToList();
        var skuKeys = needing
            .Select(x => x.Item.SellerSku!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var skuLower = skuKeys.Select(s => s.ToLowerInvariant()).ToList();

        var candidates = await (
                from v in _db.ListingVariations.AsNoTracking()
                join l in _db.Listings.AsNoTracking() on v.ListingId equals l.Id
                where v.SellerSku != null
                      && skuLower.Contains(v.SellerSku.ToLower())
                      && (ownerIds.Contains(l.OwnerId)
                          || (l.ShopId != null && shopIds.Contains(l.ShopId.Value)))
                select new
                {
                    l.OwnerId,
                    l.ShopId,
                    v.SellerSku,
                    VariationTitle = v.Title,
                    ListingTitle = l.ProductName,
                    VariationMainImage = v.MainImage,
                    ListingMainImage = l.MainImage,
                    l.UpdatedAt
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var byOwnerSku = new Dictionary<(Guid OwnerId, string SkuLower), (string? Title, string? ImgUrl)>();
        var byShopSku = new Dictionary<(Guid ShopId, string SkuLower), (string? Title, string? ImgUrl)>();
        foreach (var row in candidates.OrderByDescending(r => r.UpdatedAt))
        {
            var sku = row.SellerSku?.Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                continue;
            }

            var skuKey = sku.ToLowerInvariant();
            var title = FirstNonEmpty(row.VariationTitle, row.ListingTitle);
            var img = FirstImageUrl(row.VariationMainImage) ?? FirstImageUrl(row.ListingMainImage);
            byOwnerSku.TryAdd((row.OwnerId, skuKey), (title, img));
            if (row.ShopId is Guid shopId)
            {
                byShopSku.TryAdd((shopId, skuKey), (title, img));
            }
        }

        foreach (var (order, item) in needing)
        {
            var sku = item.SellerSku!.Trim().ToLowerInvariant();
            if (!byOwnerSku.TryGetValue((order.OwnerId, sku), out var fields)
                && !(order.ShopId is Guid sid && byShopSku.TryGetValue((sid, sku), out fields)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Title) && !string.IsNullOrWhiteSpace(fields.Title))
            {
                item.Title = fields.Title;
            }

            if (string.IsNullOrWhiteSpace(item.ImgUrl) && !string.IsNullOrWhiteSpace(fields.ImgUrl))
            {
                item.ImgUrl = fields.ImgUrl;
            }
        }
    }

    /// <summary>
    /// Overlays listing <c>SourceUrl</c> (only when missing) plus rating / supplier shop fields onto
    /// line items matched by order owner + <c>SellerSku</c>, then by <c>Order.ShopId</c> (shop-transfer safe).
    /// Read-model only — nothing is persisted on <see cref="OrderItem"/>.
    /// </summary>
    private async Task TryFillOrderItemListingOverlayAsync(
        IReadOnlyList<Order> orders,
        CancellationToken cancellationToken)
    {
        var needing = orders
            .SelectMany(o => (o.Items ?? []).Select(i => (Order: o, Item: i)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.SellerSku))
            .ToList();
        if (needing.Count == 0)
        {
            return;
        }

        var ownerIds = needing.Select(x => x.Order.OwnerId).Distinct().ToList();
        var shopIds = needing
            .Where(x => x.Order.ShopId.HasValue)
            .Select(x => x.Order.ShopId!.Value)
            .Distinct()
            .ToList();
        var skuKeys = needing
            .Select(x => x.Item.SellerSku!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var skuLower = skuKeys.Select(s => s.ToLowerInvariant()).ToList();

        var candidates = await (
                from v in _db.ListingVariations.AsNoTracking()
                join l in _db.Listings.AsNoTracking() on v.ListingId equals l.Id
                where v.SellerSku != null
                      && skuLower.Contains(v.SellerSku.ToLower())
                      && (ownerIds.Contains(l.OwnerId)
                          || (l.ShopId != null && shopIds.Contains(l.ShopId.Value)))
                      && ((l.SourceUrl != null && l.SourceUrl != "")
                          || l.AverageRating != null
                          || l.ReviewCount != null
                          || l.SupplierProductInfoId != null)
                select new
                {
                    l.OwnerId,
                    l.ShopId,
                    v.SellerSku,
                    l.SourceUrl,
                    l.AverageRating,
                    l.ReviewCount,
                    StoreTitle = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.StoreTitle
                        : null,
                    SoldByTips = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.SoldByTips
                        : null,
                    StoreUrl = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.StoreUrl
                        : null,
                    ShopRank = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.ShopRank
                        : null,
                    StoreAttention = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.StoreAttention
                        : null,
                    ShippingDays = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.ShippingDays
                        : null,
                    ShippingDayPercent = l.SupplierProductInfo != null && l.SupplierProductInfo.Seller != null
                        ? l.SupplierProductInfo.Seller.ShippingDayPercent
                        : null,
                    IsLocalProduct = l.SupplierProductInfo != null
                        ? l.SupplierProductInfo.IsLocalProduct
                        : null,
                    IsSupportQuickShip = l.SupplierProductInfo != null
                        ? l.SupplierProductInfo.IsSupportQuickShip
                        : null,
                    l.UpdatedAt
                })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        // One pass, newest listing first: a row may carry a SourceUrl without rating data (or the
        // reverse), so each side keeps its own first-win entry.
        var overlayByOwnerSku = new Dictionary<(Guid OwnerId, string SkuLower), ListingOverlayFields>();
        var sourceUrlByOwnerSku = new Dictionary<(Guid OwnerId, string SkuLower), string>();
        var overlayByShopSku = new Dictionary<(Guid ShopId, string SkuLower), ListingOverlayFields>();
        var sourceUrlByShopSku = new Dictionary<(Guid ShopId, string SkuLower), string>();
        foreach (var row in candidates.OrderByDescending(r => r.UpdatedAt))
        {
            var sku = row.SellerSku?.Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                continue;
            }

            var skuKey = sku.ToLowerInvariant();
            var ownerKey = (row.OwnerId, skuKey);
            if (!string.IsNullOrWhiteSpace(row.SourceUrl))
            {
                sourceUrlByOwnerSku.TryAdd(ownerKey, row.SourceUrl.Trim());
            }

            overlayByOwnerSku.TryAdd(
                ownerKey,
                new ListingOverlayFields(
                    row.AverageRating,
                    row.ReviewCount,
                    SupplierPdpMetrics.ResolveStoreTitle(row.StoreTitle, row.SoldByTips),
                    row.StoreUrl,
                    row.ShopRank,
                    row.StoreAttention,
                    row.ShippingDays,
                    row.ShippingDayPercent,
                    row.IsLocalProduct,
                    row.IsSupportQuickShip));

            if (row.ShopId is Guid shopId)
            {
                var shopKey = (shopId, skuKey);
                if (!string.IsNullOrWhiteSpace(row.SourceUrl))
                {
                    sourceUrlByShopSku.TryAdd(shopKey, row.SourceUrl.Trim());
                }

                overlayByShopSku.TryAdd(
                    shopKey,
                    new ListingOverlayFields(
                        row.AverageRating,
                        row.ReviewCount,
                        SupplierPdpMetrics.ResolveStoreTitle(row.StoreTitle, row.SoldByTips),
                        row.StoreUrl,
                        row.ShopRank,
                        row.StoreAttention,
                        row.ShippingDays,
                        row.ShippingDayPercent,
                        row.IsLocalProduct,
                        row.IsSupportQuickShip));
            }
        }

        foreach (var (order, item) in needing)
        {
            var sku = item.SellerSku!.Trim().ToLowerInvariant();
            var ownerKey = (order.OwnerId, sku);
            Guid? shopId = order.ShopId;

            if (string.IsNullOrWhiteSpace(item.SourceUrl))
            {
                if (sourceUrlByOwnerSku.TryGetValue(ownerKey, out var sourceUrl)
                    || (shopId is Guid sid
                        && sourceUrlByShopSku.TryGetValue((sid, sku), out sourceUrl)))
                {
                    item.SourceUrl = sourceUrl;
                }
            }

            if (!overlayByOwnerSku.TryGetValue(ownerKey, out var overlay)
                && !(shopId is Guid shopKeyId
                     && overlayByShopSku.TryGetValue((shopKeyId, sku), out overlay)))
            {
                continue;
            }

            item.AverageRating = overlay.AverageRating;
            item.ReviewCount = overlay.ReviewCount;
            item.StoreTitle = overlay.StoreTitle;
            item.StoreUrl = overlay.StoreUrl;
            item.ShopRank = overlay.ShopRank;
            item.StoreAttention = overlay.StoreAttention;
            item.ShippingDays = overlay.ShippingDays;
            item.ShippingDayPercent = overlay.ShippingDayPercent;
            item.IsLocalProduct = overlay.IsLocalProduct;
            item.IsSupportQuickShip = overlay.IsSupportQuickShip;
        }
    }

    private readonly record struct ListingOverlayFields(
        decimal? AverageRating,
        int? ReviewCount,
        string? StoreTitle,
        string? StoreUrl,
        decimal? ShopRank,
        int? StoreAttention,
        int? ShippingDays,
        decimal? ShippingDayPercent,
        bool? IsLocalProduct,
        bool? IsSupportQuickShip);

    private static string? FirstImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        foreach (var part in raw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks an existing local row by 4Seller id, else by owner+platform order code.
    /// When both keys point at different rows, keeps the 4Seller-id match and removes the duplicate.
    /// </summary>
    private Order? ResolveExistingOrder(
        long fourSellerOrderId,
        string? platformOrderId,
        IDictionary<long, Order> existingByFourSellerId,
        IDictionary<string, Order> existingByPlatformOrderId)
    {
        existingByFourSellerId.TryGetValue(fourSellerOrderId, out var byFourSeller);
        Order? byPlatform = null;
        if (platformOrderId is not null)
        {
            existingByPlatformOrderId.TryGetValue(platformOrderId, out byPlatform);
        }

        if (byFourSeller is not null && byPlatform is not null && byFourSeller.Id != byPlatform.Id)
        {
            _logger.LogWarning(
                "Merging duplicate order PlatformOrderId={PlatformOrderId}: keep FourSellerOrderId={KeepId}, remove FourSellerOrderId={RemoveId}",
                platformOrderId,
                byFourSeller.FourSellerOrderId,
                byPlatform.FourSellerOrderId);
            RemoveTrackedOrder(byPlatform, existingByFourSellerId, existingByPlatformOrderId);
            return byFourSeller;
        }

        return byFourSeller ?? byPlatform;
    }

    private Order RemapFourSellerOrderId(
        Order order,
        long newFourSellerOrderId,
        IDictionary<long, Order> existingByFourSellerId)
    {
        if (order.FourSellerOrderId == newFourSellerOrderId)
        {
            return order;
        }

        if (existingByFourSellerId.TryGetValue(newFourSellerOrderId, out var conflict) &&
            conflict.Id != order.Id)
        {
            _logger.LogWarning(
                "FourSellerOrderId remap conflict: dropping order {DropId} (oldId={OldFsId}) in favor of {KeepId} (fsId={NewFsId})",
                order.Id,
                order.FourSellerOrderId,
                conflict.Id,
                newFourSellerOrderId);
            existingByFourSellerId.Remove(order.FourSellerOrderId);
            RemoveOrderEntity(order);
            return conflict;
        }

        existingByFourSellerId.Remove(order.FourSellerOrderId);
        order.FourSellerOrderId = newFourSellerOrderId;
        existingByFourSellerId[newFourSellerOrderId] = order;
        return order;
    }

    private void RemoveTrackedOrder(
        Order order,
        IDictionary<long, Order> existingByFourSellerId,
        IDictionary<string, Order> existingByPlatformOrderId)
    {
        existingByFourSellerId.Remove(order.FourSellerOrderId);
        var key = NormalizePlatformOrderId(order.PlatformOrderId);
        if (key is not null &&
            existingByPlatformOrderId.TryGetValue(key, out var mapped) &&
            mapped.Id == order.Id)
        {
            existingByPlatformOrderId.Remove(key);
        }

        RemoveOrderEntity(order);
    }

    private void RemoveOrderEntity(Order order)
    {
        if (order.Items.Count > 0)
        {
            _db.OrderItems.RemoveRange(order.Items);
            order.Items.Clear();
        }

        _db.Orders.Remove(order);
    }

    private static string? NormalizePlatformOrderId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool IsCanceledStatus(string? platformOrderStatus, string? legacyOrderStatus = null) =>
        PlatformOrderStatuses.IsCanceledCommerce(platformOrderStatus, legacyOrderStatus);

    /// <summary>
    /// Cảnh báo khi một lượt sync 4Seller xoá mốc fulfil đã có. Mốc bị xoá là clock sự kiện thật đã
    /// mất, chỉ dựng lại được bằng đợt backfill từ payload SHEIN gốc, nên phải thấy được trong log
    /// thay vì chỉ phát hiện qua UI hiện "Transit?".
    /// </summary>
    private void LogClearedMilestones(
        Order order,
        IReadOnlyList<OrderFulfilTimeline.Cleared> cleared)
    {
        foreach (var item in cleared)
        {
            _logger.LogWarning(
                "Sync 4Seller xoá mốc {Milestone} ({Value:O}) của đơn {OrderNo}: {Reason}",
                item.Milestone,
                item.Value,
                order.PlatformOrderId,
                item.Reason);
        }
    }

    private static IReadOnlyList<OrderFulfilTimeline.Cleared> ApplyRecord(
        Order order,
        FourSellerOrderRecordDto record,
        Guid ownerId,
        Guid? localShopId,
        DateTime now,
        bool assignRawPayload)
    {
        order.OwnerId = ownerId;
        // Keep previous link when this sync cannot resolve a shop (avoid wiping ShopId).
        order.ShopId = localShopId ?? order.ShopId;
        order.FourSellerShopId = record.ShopId;
        order.PlatformOrderId = NormalizePlatformOrderId(
            FirstNonEmpty(record.PlatformOrderId, record.PlatformOrderNo));
        order.Platform = record.Platform;
        OrderFourSellerStatusApply.ApplyOrderStatusFromRecord(order, record.OrderStatus);

        // Refund state (PlatformOrderStatus REFUND, RefundedAt, CancelReason) belongs to the
        // after-sales sync — this flow only mirrors the commerce status and latches REFUND.
        var resolvedStatus = PlatformOrderStatuses.ResolveCommerceStatus(
            record.PlatformOrderStatus,
            order.PlatformOrderStatus);
        order.PlatformOrderStatus = resolvedStatus ?? order.PlatformOrderStatus;
        order.Amount = TikTokSellerNetCalculator.KeepLastNonZero(order.Amount, record.Amount);
        order.TotalPaidAmount = TikTokSellerNetCalculator.KeepLastNonZero(
            order.TotalPaidAmount, record.TotalPaidAmount);
        order.Currency = string.IsNullOrWhiteSpace(record.Currency) ? "USD" : record.Currency.Trim();
        order.ShipmentNo = record.ShipmentNo;
        order.ShippingMethod = record.ShippingMethod;
        OrderFourSellerStatusApply.ApplyTrackingFromRecord(order, record);
        PromoteAutoMarkShipFromPublishedOrder(order);

        order.BuyerMemo = record.BuyerMemo;
        order.SellerMemo = record.SellerMemo;
        order.InternalMemo = record.InternalMemo;
        if (assignRawPayload)
        {
            order.RawPayload = record.RawJson;
        }

        var addr = record.OrderAddressVo;
        if (addr is not null)
        {
            order.BuyerName = addr.Name;
            order.BuyerPhone = addr.Phone;
            order.BuyerEmail = addr.Email;
            order.Address = FirstNonEmpty(addr.Address, addr.Address1);
            order.City = addr.City;
            order.State = addr.State;
            order.PostalCode = addr.PostalCode;
            order.CountryCode = addr.CountryCode;
        }

        var time = record.TimeInfo;
        if (time is not null)
        {
            order.OrderPaidTime = ParseFourSellerTime(time.OrderPaidTime);
            order.OrderCreateTime = ParseFourSellerTime(time.OrderCreateTime);
            order.LatestShipDate = ParseFourSellerTime(time.LatestShipDate);
            order.OrderShippedTime = ParseFourSellerTime(time.OrderShippedTime);
            order.LatestDeliveryDate = ParseFourSellerTime(time.LatestDeliveryDate);
        }

        order.IsCustomerPaid = order.OrderPaidTime.HasValue;

        var pkg = record.PackageInfo;
        if (pkg is not null)
        {
            order.PackageWeight = pkg.Weight;
            order.PackageWeightUnit = pkg.WeightUnit;
            order.PackageLength = pkg.Length;
            order.PackageWidth = pkg.Width;
            order.PackageHeight = pkg.Height;
            order.PackageDimensionUnit = pkg.DimensionUnit;
        }

        return OrderFourSellerStatusApply.ApplyPlatformClocks(order, time);
    }

    /// <summary>
    /// Reconciles order line items with the 4Seller payload. Returns <c>true</c> when items were
    /// added or removed (structural change invisible to the change tracker after removal).
    /// Empty/omitted <c>orderItemInfoList</c> keeps local rows — list pages sometimes omit items.
    /// </summary>
    private bool SyncItems(
        Order order,
        IReadOnlyList<FourSellerOrderItemDto> remoteItems,
        DateTime now)
    {
        if (!OrderItemSyncApply.HasRemoteItems(remoteItems))
        {
            return false;
        }

        var structuralChanged = false;
        var byRemoteId = OrderItemSyncApply.IndexByRemoteId(order.Items);
        var seen = new HashSet<long>();

        foreach (var remote in remoteItems.Where(i => i.OrderItemId > 0))
        {
            seen.Add(remote.OrderItemId);
            if (byRemoteId.TryGetValue(remote.OrderItemId, out var local))
            {
                ApplyItem(local, remote);
            }
            else
            {
                var item = new OrderItem
                {
                    FourSellerOrderItemId = remote.OrderItemId,
                    OrderId = order.Id,
                    CreatedAt = now
                };
                ApplyItem(item, remote);
                order.Items.Add(item);
                // Guid PK is set on new(); Entry() would attach Unchanged and SaveChanges
                // would UPDATE 0 rows. Add() forces INSERT.
                _db.OrderItems.Add(item);
                structuralChanged = true;
            }
        }

        var stale = order.Items.Where(i => !seen.Contains(i.FourSellerOrderItemId)).ToList();
        foreach (var item in stale)
        {
            order.Items.Remove(item);
            _db.OrderItems.Remove(item);
            structuralChanged = true;
        }

        return structuralChanged;
    }

    private static void ApplyItem(OrderItem item, FourSellerOrderItemDto remote)
    {
        item.SellerSku = remote.SellerSku;
        // Keep previously enriched Title/ImgUrl when 4Seller omits them (avoids wipe→refill thrash).
        if (!string.IsNullOrWhiteSpace(remote.Title))
        {
            item.Title = remote.Title;
        }

        if (!string.IsNullOrWhiteSpace(remote.ImgUrl))
        {
            item.ImgUrl = remote.ImgUrl;
        }

        item.ListingUrl = remote.ListingUrl;
        // Keep previously enriched / local source when 4Seller omits it.
        if (!string.IsNullOrWhiteSpace(remote.SourceUrl))
        {
            item.SourceUrl = remote.SourceUrl;
        }

        item.VariantAttr = remote.VariantAttr;
        item.Quantity = remote.Quantity;
        item.ItemPrice = TikTokSellerNetCalculator.KeepLastNonZero(item.ItemPrice, remote.ItemPrice);
        item.Currency = remote.Currency;
    }

    private static OrderItemDto MapItem(OrderItem i) => new()
    {
        Id = i.Id,
        FourSellerOrderItemId = i.FourSellerOrderItemId,
        SellerSku = i.SellerSku,
        Title = i.Title,
        ImgUrl = i.ImgUrl,
        SourceImgUrl = i.SourceImgUrl,
        AverageRating = i.AverageRating,
        ReviewCount = i.ReviewCount,
        StoreTitle = i.StoreTitle,
        StoreUrl = i.StoreUrl,
        ShopRank = i.ShopRank,
        StoreAttention = i.StoreAttention,
        ShippingDays = i.ShippingDays,
        ShippingDayPercent = i.ShippingDayPercent,
        IsLocalProduct = i.IsLocalProduct,
        IsSupportQuickShip = i.IsSupportQuickShip,
        ListingUrl = i.ListingUrl,
        SourceUrl = i.SourceUrl,
        VariantAttr = i.VariantAttr,
        Quantity = i.Quantity,
        ItemPrice = i.ItemPrice,
        Currency = i.Currency
    };

    private async Task ApplySettlementPaidAsync(
        Order order,
        bool isSettlementPaid,
        Guid actingUserId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ReceivingBankTransactions
            .FirstOrDefaultAsync(
                t => t.OrderId == order.Id && t.Source == ReceivingBankTransaction.SourceSettlementPaid,
                cancellationToken);

        if (!isSettlementPaid)
        {
            // Payout-synced orders stay paid on the ledger (TikTokPayout row); only clear flag
            // when there is no TikTok payment link.
            if (!string.IsNullOrWhiteSpace(order.TikTokPaymentId))
            {
                throw new BusinessException(
                    "Đơn đã gắn TikTok payout — không gỡ Đã thanh toán thủ công. " +
                    "Xóa/điều chỉnh dòng TikTokPayout trên sổ cái nếu cần.");
            }

            if (existing != null)
            {
                _db.ReceivingBankTransactions.Remove(existing);
            }

            order.IsSettlementPaid = false;
            return;
        }

        // Already booked via TikTok payout sync — keep flag, do not add SettlementPaid ledger row.
        if (!string.IsNullOrWhiteSpace(order.TikTokPaymentId))
        {
            if (existing != null)
            {
                _db.ReceivingBankTransactions.Remove(existing);
            }

            order.IsSettlementPaid = true;
            return;
        }

        if (!order.ShopId.HasValue)
        {
            throw new BusinessException("Đơn hàng chưa gắn shop, không thể đánh dấu Đã thanh toán.");
        }

        var shop = order.Shop
            ?? await _db.Shops.FirstOrDefaultAsync(s => s.Id == order.ShopId.Value, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        if (!shop.ReceivingBankAccountId.HasValue)
        {
            throw new BusinessException("Shop chưa gắn thẻ nhận tiền.");
        }

        var cardActive = await _db.ReceivingBankAccounts.AnyAsync(
            a => a.Id == shop.ReceivingBankAccountId.Value,
            cancellationToken);
        if (!cardActive)
        {
            throw new NotFoundException("Không tìm thấy thẻ nhận tiền.");
        }

        var received = ResolveReceivedAmount(order);
        var currency = string.IsNullOrWhiteSpace(order.Currency) ? "USD" : order.Currency;
        var note = $"Thanh toán đơn {order.PlatformOrderId ?? order.FourSellerOrderId.ToString()}";

        if (existing is null)
        {
            _db.ReceivingBankTransactions.Add(new ReceivingBankTransaction
            {
                ReceivingBankAccountId = shop.ReceivingBankAccountId.Value,
                ShopId = shop.Id,
                OrderId = order.Id,
                Amount = received,
                Direction = ReceivingBankTransaction.DirectionCredit,
                Currency = currency,
                Source = ReceivingBankTransaction.SourceSettlementPaid,
                ConfirmationStatus = ReceivingBankTransaction.ConfirmationConfirmed,
                CreatedByUserId = actingUserId,
                OccurredAt = DateTime.UtcNow,
                Note = note
            });
        }
        else
        {
            existing.ReceivingBankAccountId = shop.ReceivingBankAccountId.Value;
            existing.ShopId = shop.Id;
            existing.Amount = received;
            existing.Direction = ReceivingBankTransaction.DirectionCredit;
            existing.Currency = currency;
            existing.ConfirmationStatus = ReceivingBankTransaction.ConfirmationConfirmed;
            existing.OccurredAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.Note = note;
        }

        order.IsSettlementPaid = true;
    }

    private static decimal ResolveReceivedAmount(Order order) => OrderRevenue.Resolve(order);

    private static int ResolveTotalQuantity(IEnumerable<OrderItem> items)
    {
        var sum = items.Sum(i => i.Quantity);
        return sum < 1 ? 1 : sum;
    }

    private static OrderListItemDto MapListItem(Order o)
    {
        var lineItems = (o.Items ?? [])
            .OrderBy(i => i.FourSellerOrderItemId)
            .Select(MapItem)
            .ToList();
        var totalQuantity = ResolveTotalQuantity(o.Items ?? []);

        // Ưu tiên cột Charged* (materialize lúc sync); fallback SUM nếu đơn chưa backfill.
        var purchases = (o.SupplierPurchases ?? []).OrderBy(p => p.CreatedAt).ToList();
        var primary = OrderSupplierCost.PrimaryPurchase(purchases);
        var supplierTotal = o.ChargedSupplierTotal
            ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierTotal);
        var supplierWallet = o.ChargedSupplierWalletPrice
            ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierWalletPrice);

        return new OrderListItemDto
        {
            Id = o.Id,
            FourSellerOrderId = o.FourSellerOrderId,
            PlatformOrderId = o.PlatformOrderId,
            Platform = o.Platform,
            OrderStatus = o.OrderStatus,
            PlatformOrderStatus = o.PlatformOrderStatus,
            CancelReason = o.CancelReason,
            ReturnStatus = o.ReturnStatus,
            Message = o.Message,
            IssueStatus = o.IssueStatus ?? OrderIssueStatuses.None,
            IssueTags = (o.IssueTags ?? [])
                .Where(x => x.IssueTag != null)
                .Select(x => new IssueTagDto
                {
                    Id = x.IssueTag.Id,
                    Label = x.IssueTag.Label,
                    IsSystem = x.IssueTag.IsSystem,
                })
                .OrderByDescending(t => t.IsSystem)
                .ThenBy(t => t.Label)
                .ToList(),
            WorkflowTab = OrderWorkflowTabs.Resolve(o),
            Amount = o.Amount,
            TotalPaidAmount = o.TotalPaidAmount,
            ProductAmountBeforeDiscount = o.ProductAmountBeforeDiscount,
            ProductPlatformDiscount = o.ProductPlatformDiscount,
            ProductSellerDiscount = o.ProductSellerDiscount,
            ShippingFeeBeforeDiscount = o.ShippingFeeBeforeDiscount,
            ShippingFeeSellerDiscount = o.ShippingFeeSellerDiscount,
            PlatformTotalTax = o.PlatformTotalTax,
            PlatformOtherFee = o.PlatformOtherFee,
            SellerNetAmount = o.SellerNetAmount,
            PricingDetailSyncedAt = o.PricingDetailSyncedAt,
            Currency = o.Currency,
            Cost = o.Cost,
            ShippingFee = o.ShippingFee,
            Tax = o.Tax,
            Ads = o.Ads,
            SupplierOrderNo = primary?.SupplierOrderNo,
            SupplierOrderUrl = primary?.SupplierOrderUrl,
            SupplierRetailPrice = o.ChargedSupplierRetailPrice
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierRetailPrice),
            SupplierSubtotal = o.ChargedSupplierSubtotal
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierSubtotal),
            SupplierShippingFee = o.ChargedSupplierShippingFee
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierShippingFee),
            SupplierRetailDeliveryFee = o.ChargedSupplierRetailDeliveryFee
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierRetailDeliveryFee),
            SupplierOnTimeGuarantee = o.ChargedSupplierOnTimeGuarantee
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierOnTimeGuarantee),
            SupplierCouponPrice = o.ChargedSupplierCouponPrice
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierCouponPrice),
            SupplierMembershipDiscount = o.ChargedSupplierMembershipDiscount
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierMembershipDiscount),
            SupplierPointsDiscount = o.ChargedSupplierPointsDiscount
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierPointsDiscount),
            SupplierWalletPrice = supplierWallet,
            SupplierTax = o.ChargedSupplierTax
                ?? OrderSupplierCost.SumCharged(purchases, p => p.SupplierTax),
            SupplierTotal = supplierTotal,
            TotalQuantity = totalQuantity,
            // TotalCost = gross NCC + phí xử lý BuyTrack / ThankCard.
            TotalCost = OrderSupplierCost.ResolveOrderTotalCost(o),
            TrackingNo = o.TrackingNo,
            ShipmentNo = o.ShipmentNo,
            ShippingMethod = o.ShippingMethod,
            ShippingService = o.ShippingService,
            OrderCreateTime = o.OrderCreateTime,
            OrderPaidTime = o.OrderPaidTime,
            LatestShipDate = o.LatestShipDate,
            LastSyncedAt = o.LastSyncedAt,
            OwnerId = o.OwnerId,
            OwnerName = string.IsNullOrWhiteSpace(o.Owner?.FullName) ? o.Owner?.UserName : o.Owner?.FullName,
            ShopId = o.ShopId,
            ShopName = o.Shop?.Name,
            ShopProfileName = o.Shop?.ProfileName,
            BuyerName = o.BuyerName,
            BuyerPhone = o.BuyerPhone,
            BuyerEmail = o.BuyerEmail,
            Address = o.Address,
            City = o.City,
            State = o.State,
            PostalCode = o.PostalCode,
            CountryCode = o.CountryCode,
            BuyerMemo = o.BuyerMemo,
            SellerMemo = o.SellerMemo,
            InternalMemo = o.InternalMemo,
            FulfilAccountId = o.FulfilAccountId,
            FulfilAccountName = o.FulfilAccount?.Name,
            FulfilCardId = o.FulfilCardId,
            FulfilCardName = o.FulfilCard?.Name,
            FulfilCardNumberMasked = o.FulfilCard == null ? null : FulfilService.MaskCardNumber(o.FulfilCard.CardNumber),
            PaidByUserId = o.PaidByUserId,
            PaidByUserName = string.IsNullOrWhiteSpace(o.PaidByUser?.FullName)
                ? o.PaidByUser?.UserName
                : o.PaidByUser?.FullName,
            FulfilClaimedByUserId = IsClaimActive(o) ? o.FulfilClaimedByUserId : null,
            FulfilClaimedByName = IsClaimActive(o)
                ? (string.IsNullOrWhiteSpace(o.FulfilClaimedByUser?.FullName)
                    ? o.FulfilClaimedByUser?.UserName
                    : o.FulfilClaimedByUser?.FullName)
                : null,
            FulfilClaimedAt = IsClaimActive(o) ? o.FulfilClaimedAt : null,
            FulfilClaimExpiresAt = IsClaimActive(o) ? o.FulfilClaimExpiresAt : null,
            FulfilClaimSessionCount = IsClaimActive(o) ? o.FulfilClaimSessionCount : 0,
            FulfilPurchasedAt = o.FulfilPurchasedAt,
            TrackingAddedAt = o.TrackingAddedAt,
            InTransitAt = o.InTransitAt,
            DeliveredAt = o.DeliveredAt,
            RefundedAt = o.RefundedAt,
            UspsProcessedAt = o.UspsProcessedAt,
            UspsScanBatchId = o.UspsScanBatchId,
            UspsScanSentAt = o.UspsScanSentAt,
            BuyTrackAmount = o.BuyTrackAmount,
            ThankCardProcessedAt = o.ThankCardProcessedAt,
            ThankCardAmount = o.ThankCardAmount,
            RefundProcessedAt = o.RefundProcessedAt,
            CanceledAt = o.CanceledAt,
            IsSettlementPaid = o.IsSettlementPaid,
            IsCustomerPaid = o.IsCustomerPaid,
            PackageWeight = o.PackageWeight,
            PackageWeightUnit = o.PackageWeightUnit,
            PackageLength = o.PackageLength,
            PackageWidth = o.PackageWidth,
            PackageHeight = o.PackageHeight,
            PackageDimensionUnit = o.PackageDimensionUnit,
            ItemCount = lineItems.Count,
            LineItems = lineItems,
            SupplierLinks = purchases
                .Select(l => new OrderSupplierLinkDto
                {
                    Id = l.Id,
                    IsCharged = l.IsCharged,
                    Source = l.Source,
                    SupplierOrderNo = l.SupplierOrderNo ?? string.Empty,
                    RelationBillno = l.RelationBillno,
                    ConfluenceBatchNo = l.ConfluenceBatchNo,
                    SupplierOrderUrl = l.SupplierOrderUrl,
                    TrackingNo = l.TrackingNo,
                    WarehouseTrackingNo = l.WarehouseTrackingNo,
                    WarehouseTrackingSeenAt = l.WarehouseTrackingSeenAt,
                    Carrier = l.Carrier,
                    TrackingKind = l.TrackingKind,
                    SupplierTotal = l.SupplierTotal,
                    SupplierRetailPrice = l.SupplierRetailPrice,
                    SupplierSubtotal = l.SupplierSubtotal,
                    SupplierShippingFee = l.SupplierShippingFee,
                    SupplierRetailDeliveryFee = l.SupplierRetailDeliveryFee,
                    SupplierOnTimeGuarantee = l.SupplierOnTimeGuarantee,
                    SupplierCouponPrice = l.SupplierCouponPrice,
                    SupplierMembershipDiscount = l.SupplierMembershipDiscount,
                    SupplierPointsDiscount = l.SupplierPointsDiscount,
                    SupplierWalletPrice = l.SupplierWalletPrice,
                    SupplierTax = l.SupplierTax,
                    SupplierStatus = l.SupplierStatus,
                    SupplierStatusRaw = l.SupplierStatusRaw,
                    SupplierStatusAt = l.SupplierStatusAt,
                    SupplierUnpaid = l.SupplierUnpaid,
                    AutoMarkShipTrackingNo = l.AutoMarkShipTrackingNo,
                    AutoMarkShipStatus = l.AutoMarkShipStatus,
                    AutoMarkShipAttemptedAt = l.AutoMarkShipAttemptedAt,
                    AutoMarkShipError = l.AutoMarkShipError,
                    AutoMarkShipRequestId = l.AutoMarkShipRequestId
                })
                .ToList()
        };
    }

    private static OrderDetailDto MapDetail(Order o)
    {
        var list = MapListItem(o);
        return new OrderDetailDto
        {
            Id = list.Id,
            FourSellerOrderId = list.FourSellerOrderId,
            PlatformOrderId = list.PlatformOrderId,
            Platform = list.Platform,
            OrderStatus = list.OrderStatus,
            PlatformOrderStatus = list.PlatformOrderStatus,
            CancelReason = list.CancelReason,
            ReturnStatus = list.ReturnStatus,
            Message = list.Message,
            IssueStatus = list.IssueStatus,
            IssueTags = list.IssueTags,
            WorkflowTab = list.WorkflowTab,
            Amount = list.Amount,
            TotalPaidAmount = list.TotalPaidAmount,
            ProductAmountBeforeDiscount = list.ProductAmountBeforeDiscount,
            ProductPlatformDiscount = list.ProductPlatformDiscount,
            ProductSellerDiscount = list.ProductSellerDiscount,
            ShippingFeeBeforeDiscount = list.ShippingFeeBeforeDiscount,
            ShippingFeeSellerDiscount = list.ShippingFeeSellerDiscount,
            PlatformTotalTax = list.PlatformTotalTax,
            PlatformOtherFee = list.PlatformOtherFee,
            SellerNetAmount = list.SellerNetAmount,
            PricingDetailSyncedAt = list.PricingDetailSyncedAt,
            Currency = list.Currency,
            Cost = list.Cost,
            ShippingFee = list.ShippingFee,
            Tax = list.Tax,
            Ads = list.Ads,
            SupplierOrderNo = list.SupplierOrderNo,
            SupplierOrderUrl = list.SupplierOrderUrl,
            SupplierRetailPrice = list.SupplierRetailPrice,
            SupplierSubtotal = list.SupplierSubtotal,
            SupplierShippingFee = list.SupplierShippingFee,
            SupplierRetailDeliveryFee = list.SupplierRetailDeliveryFee,
            SupplierOnTimeGuarantee = list.SupplierOnTimeGuarantee,
            SupplierCouponPrice = list.SupplierCouponPrice,
            SupplierMembershipDiscount = list.SupplierMembershipDiscount,
            SupplierPointsDiscount = list.SupplierPointsDiscount,
            SupplierWalletPrice = list.SupplierWalletPrice,
            SupplierTax = list.SupplierTax,
            SupplierTotal = list.SupplierTotal,
            TotalQuantity = list.TotalQuantity,
            TotalCost = list.TotalCost,
            TrackingNo = list.TrackingNo,
            ShipmentNo = list.ShipmentNo,
            ShippingMethod = list.ShippingMethod,
            ShippingService = list.ShippingService,
            OrderCreateTime = list.OrderCreateTime,
            OrderPaidTime = list.OrderPaidTime,
            LatestShipDate = list.LatestShipDate,
            LastSyncedAt = list.LastSyncedAt,
            OwnerId = list.OwnerId,
            OwnerName = list.OwnerName,
            ShopId = list.ShopId,
            ShopName = list.ShopName,
            ShopProfileName = list.ShopProfileName,
            BuyerName = list.BuyerName,
            BuyerPhone = list.BuyerPhone,
            BuyerEmail = list.BuyerEmail,
            Address = list.Address,
            City = list.City,
            State = list.State,
            PostalCode = list.PostalCode,
            CountryCode = list.CountryCode,
            BuyerMemo = list.BuyerMemo,
            SellerMemo = list.SellerMemo,
            InternalMemo = list.InternalMemo,
            FulfilAccountId = list.FulfilAccountId,
            FulfilAccountName = list.FulfilAccountName,
            FulfilCardId = list.FulfilCardId,
            FulfilCardName = list.FulfilCardName,
            FulfilCardNumberMasked = list.FulfilCardNumberMasked,
            PaidByUserId = list.PaidByUserId,
            PaidByUserName = list.PaidByUserName,
            FulfilClaimedByUserId = list.FulfilClaimedByUserId,
            FulfilClaimedByName = list.FulfilClaimedByName,
            FulfilClaimedAt = list.FulfilClaimedAt,
            FulfilClaimExpiresAt = list.FulfilClaimExpiresAt,
            FulfilClaimSessionCount = list.FulfilClaimSessionCount,
            FulfilPurchasedAt = list.FulfilPurchasedAt,
            TrackingAddedAt = list.TrackingAddedAt,
            InTransitAt = list.InTransitAt,
            DeliveredAt = list.DeliveredAt,
            RefundedAt = list.RefundedAt,
            UspsProcessedAt = list.UspsProcessedAt,
            UspsScanBatchId = list.UspsScanBatchId,
            UspsScanSentAt = list.UspsScanSentAt,
            BuyTrackAmount = list.BuyTrackAmount,
            ThankCardProcessedAt = list.ThankCardProcessedAt,
            ThankCardAmount = list.ThankCardAmount,
            RefundProcessedAt = list.RefundProcessedAt,
            CanceledAt = list.CanceledAt,
            IsSettlementPaid = list.IsSettlementPaid,
            IsCustomerPaid = list.IsCustomerPaid,
            PackageWeight = list.PackageWeight,
            PackageWeightUnit = list.PackageWeightUnit,
            PackageLength = list.PackageLength,
            PackageWidth = list.PackageWidth,
            PackageHeight = list.PackageHeight,
            PackageDimensionUnit = list.PackageDimensionUnit,
            ItemCount = list.ItemCount,
            LineItems = list.LineItems,
            SupplierLinks = list.SupplierLinks,
            OrderShippedTime = o.OrderShippedTime,
            LatestDeliveryDate = o.LatestDeliveryDate,
            Items = list.LineItems
        };
    }


    /// <summary>
    /// Street-only key like buyer-box Address field: strip trailing city/state/zip/country
    /// when the address blob still embeds them, then normalize.
    /// </summary>
    private static string StreetKey(
        string? address,
        string? city,
        string? state,
        string? postalCode,
        string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(address)) return string.Empty;
        var street = address.Trim();
        foreach (var part in new[] { countryCode, "United States", "USA", "US", postalCode, state, city })
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var token = part.Trim();
            street = System.Text.RegularExpressions.Regex.Replace(
                street,
                @"[,\s]+" + System.Text.RegularExpressions.Regex.Escape(token) + @"\s*$",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return NormalizeText(street);
    }

    /// <summary>
    /// Unmatched due to Shipping phone/zip — extension should detail-enrich and rematch.
    /// Do not queue membership/unpaid/terminal skips here.
    /// </summary>
    private static void MarkSheinUnmatchedForAddressRematch(
        SheinOrderSyncResultDto result,
        string billno,
        string reason,
        string? detail = null)
    {
        AddSheinSkip(result, billno, reason, detail);
        var trimmed = billno.Trim();
        if (trimmed.Length == 0) return;
        if (result.UnmatchedBillnos.Exists(b =>
                string.Equals(b, trimmed, StringComparison.OrdinalIgnoreCase)))
            return;
        result.UnmatchedBillnos.Add(trimmed);
    }

    /// <summary>Ensures DateTime is UTC kind for storage.</summary>
    internal static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Applies an explicit human milestone edit: clears any prior value/provenance so the
    /// Manual-ranked timestamp always wins, then stamps. No-op when the request omits the field.
    /// </summary>
    private static void StampManualMilestone(
        Order order, OrderFulfilTimeline.Milestone milestone, DateTime? value)
    {
        if (value is null) return;
        OrderFulfilTimeline.Clear(order, milestone);
        OrderFulfilTimeline.Stamp(
            order, milestone, NormalizeUtc(value.Value), OrderFulfilTimeline.Source.Manual);
    }

    /// <summary>
    /// 4Seller returned timestamps are naive strings that are already UTC digits —
    /// same contract as <see cref="Shared.Helpers.AppTimeZones.ParseFourSellerNaiveAsUtc"/>.
    /// </summary>
    private static DateTime? ParseFourSellerTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
