using System.Linq.Expressions;
using Application.DTOs.Order;
using Application.Services.Shein;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Bước 3 của luồng SHEIN: <b>hàng đợi gắn tay</b>.
///
/// Khớp tự động dựa trên phone + zip trùng tuyệt đối, nên mọi ca lệch một ký tự đều trượt. Trước
/// đây trượt là mất luôn — không có bảng nào chứa được một lần mua chưa thuộc đơn nào. Giờ dòng đó
/// nằm trong <c>SupplierPurchases</c> với <c>OrderId = null</c>, và bốn thao tác dưới đây là đường
/// để người xử lý kết thúc nốt phần máy không quyết được.
/// </summary>
public partial class OrderSyncService
{
    /// <inheritdoc />
    public async Task<SupplierPurchaseListPageDto> ListSupplierPurchasesAsync(
        SupplierPurchaseQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = query.PageCurrent < 1 ? 1 : query.PageCurrent;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);

        var filtered = ApplySupplierPurchaseFilters(_db.SupplierPurchases.AsNoTracking(), query);
        var total = await filtered.CountAsync(cancellationToken);

        var items = await filtered
            // Dòng chưa gắn lên đầu: đó là thứ trang này tồn tại để xử lý. Mốc mua giảm dần trong
            // từng nhóm, tie-break theo Id để hai lần mở cùng bộ lọc cho cùng thứ tự.
            .OrderBy(p => p.MatchState == SupplierPurchaseMatchStates.Unmatched ? 0 : 1)
            .ThenByDescending(p => p.SupplierPaidAt ?? p.OrderedAt ?? p.FirstSeenAt)
            .ThenBy(p => p.Id)
            .Skip((pageCurrent - 1) * pageSize)
            .Take(pageSize)
            .Select(PurchaseProjection)
            .ToListAsync(cancellationToken);

        // Hai con số này cố tình **không** theo bộ lọc trang: chúng là badge tổng để người xử lý biết
        // còn bao nhiêu việc, kể cả khi đang xem một bộ lọc hẹp.
        var stateCounts = await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.MatchState != SupplierPurchaseMatchStates.Matched)
            .GroupBy(p => p.MatchState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new SupplierPurchaseListPageDto
        {
            Items = items,
            Total = total,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            UnmatchedCount = stateCounts
                .FirstOrDefault(x => x.State == SupplierPurchaseMatchStates.Unmatched)?.Count ?? 0,
            IgnoredCount = stateCounts
                .FirstOrDefault(x => x.State == SupplierPurchaseMatchStates.Ignored)?.Count ?? 0
        };
    }

    /// <inheritdoc />
    public async Task<SupplierPurchaseDto> AttachSupplierPurchaseAsync(
        Guid id,
        Guid orderId,
        Guid actingUserId,
        Guid? fulfilAccountId = null,
        CancellationToken cancellationToken = default)
    {
        var purchase = await LoadPurchaseForWriteAsync(id, cancellationToken);
        var order = await _db.Orders
            .Include(o => o.SupplierPurchases)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        var now = DateTime.UtcNow;

        // Email billing SHEIN không map được tài khoản nào thì lần mua vào hàng đợi mà không có quy
        // kết tiền, và thao tác gắn là lúc duy nhất người xử lý đang nhìn đủ bằng chứng để chỉ ra
        // nó. Chỉ điền vào chỗ trống: đổi tài khoản của một lần mua đã quy kết là sửa sổ cái, phải
        // đi đường sửa dòng chi chứ không núp sau nút Gắn.
        if (fulfilAccountId.HasValue)
        {
            if (purchase.FulfilAccountId.HasValue && purchase.FulfilAccountId != fulfilAccountId)
            {
                throw new BusinessException(
                    "Lần mua này đã quy kết tài khoản mua hàng khác — sửa trên sổ cái, không đổi qua thao tác gắn.");
            }

            if (purchase.FulfilAccountId is null)
            {
                var accountExists = await _db.FulfilAccounts
                    .AnyAsync(a => a.Id == fulfilAccountId.Value, cancellationToken);
                if (!accountExists)
                {
                    throw new NotFoundException("Không tìm thấy tài khoản mua hàng.");
                }

                purchase.FulfilAccountId = fulfilAccountId;
                purchase.FulfilCardId = await ResolveActiveCardIdAsync(fulfilAccountId, cancellationToken);
            }
        }

        var previousOrder = purchase.OrderId == orderId ? null : await LoadOrderGraphAsync(purchase.OrderId, cancellationToken);

        // Gắn sang đơn mới **trước** khi dựng lại sổ cái đơn cũ: vòng dọn của reconcile đọc
        // `purchase.OrderId` để biết dòng chi đã chuyển chủ hay đã rời hẳn đơn. Làm ngược lại thì
        // đơn cũ xoá dòng chi, entity vào state Deleted, và đơn mới không dựng lại được nó
        // (unique theo lần mua) — khoản tiền thật biến mất khỏi sổ cái.
        previousOrder?.SupplierPurchases?.Remove(purchase);
        SupplierPurchaseIngest.Attach(purchase, order, now, actingUserId);

        // Người xử lý đã tra ra đơn đúng — khoá lại để lượt sync sau không khớp lại theo
        // phone+zip+tên rồi kéo billno về đơn cũ.
        purchase.ManualLinkedAt = now;

        // Mốc "Mua" đi theo lần mua: stamp đơn mới từ clock SHEIN thật (không dùng giờ bấm nút),
        // gỡ mốc trên đơn cũ khi nó bắt nguồn từ chính lần mua vừa dời.
        var purchasedAt = purchase.SupplierPaidAt
            ?? (purchase.SupplierUnpaid ? null : purchase.OrderedAt);
        if (purchasedAt.HasValue)
        {
            OrderFulfilTimeline.Stamp(
                order,
                OrderFulfilTimeline.Milestone.FulfilPurchased,
                purchasedAt.Value,
                OrderFulfilTimeline.Source.Shein);
            OrderFulfilTimeline.Normalize(order);
        }

        if (previousOrder is not null)
        {
            // ClearPurchasedWhenLinkMovedAway hẹp (cần khớp clock lần mua vừa dời). Khi không còn
            // lần mua đã thanh toán, ClearMilestonesWithoutLinkEvidence là lớp bảo hiểm — tránh đơn
            // cũ giữ "Mua"/"Transit" vì lệch millisecond hoặc mốc stamp từ field khác. Không gọi khi
            // vẫn còn lần mua đã trả: hàm đó xoá mốc chỉ vì clock không khớp dòng còn lại, làm mất
            // bằng chứng "Đã mua" dù tiền còn trên đơn.
            OrderPurchaseEvidence.ClearPurchasedWhenLinkMovedAway(previousOrder, purchase);
            var hasPaidRemaining = previousOrder.SupplierPurchases?.Any(l => !l.SupplierUnpaid) == true;
            if (!hasPaidRemaining)
            {
                OrderSheinMilestoneEvidence.ClearMilestonesWithoutLinkEvidence(previousOrder);
            }

            // Gắn sang đơn khác là một phát biểu rằng lần khớp trước sai, nên đơn cũ phải mất tiền
            // ngay trong cùng thao tác — bằng không cùng một khoản chi nằm trên hai đơn.
            OrderSupplierCost.ApplyAggregates(previousOrder);
            SyncFulfilAttributionWithPurchases(previousOrder);
            await ReconcileCostUpdateTransactionsAsync(previousOrder, actingUserId, null, cancellationToken);
            previousOrder.UpdatedAt = now;
        }

        OrderSupplierCost.ApplyAggregates(order);

        // Quy kết tiền đi theo lần mua, và danh sách đơn đọc bản sao cấp đơn của nó. Không lan ở
        // đây thì gắn tay xong sổ cái ghi đúng tài khoản trong khi thẻ đơn trống cột «TK mua» —
        // đúng ca GSU1SG240001UUP trên đơn 577590806038024769. Phải chạy **trước** reconcile:
        // ReconcileCostUpdateTransactionsAsync đọc `order.FulfilAccountId` để suy thẻ cho dòng chi.
        SyncFulfilAttributionWithPurchases(order);

        // Người mua của lần mua là người mua của đơn khi đơn chưa có ai — cùng lý do với quy kết
        // tài khoản, cột Fulfill của đơn không được để trống sau một thao tác gắn thành công.
        order.PaidByUserId ??= purchase.PaidByUserId;

        await ReconcileCostUpdateTransactionsAsync(order, actingUserId, null, cancellationToken);
        order.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);
        return await ReadPurchaseAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SupplierPurchaseDto> DetachSupplierPurchaseAsync(
        Guid id,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var purchase = await LoadPurchaseForWriteAsync(id, cancellationToken);
        if (purchase.OrderId is null)
        {
            return await ReadPurchaseAsync(id, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var order = await LoadOrderGraphAsync(purchase.OrderId, cancellationToken);

        // Dòng nhập tay không có nguồn nào ngoài người nhập, gỡ khỏi đơn là nó không còn nghĩa gì —
        // xoá hẳn thay vì để lại một dòng rỗng trong hàng đợi.
        if (purchase.Source == SupplierPurchaseSources.Manual)
        {
            order?.SupplierPurchases?.Remove(purchase);
            _db.SupplierPurchases.Remove(purchase);
        }
        else
        {
            SupplierPurchaseIngest.Detach(purchase, order, SheinSkipReasons.ManualDetach, now);
        }

        if (order is not null)
        {
            OrderSupplierCost.ApplyAggregates(order);
            SyncFulfilAttributionWithPurchases(order);
            await ReconcileCostUpdateTransactionsAsync(order, actingUserId, null, cancellationToken);
            order.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return purchase.Source == SupplierPurchaseSources.Manual
            ? new SupplierPurchaseDto { Id = id }
            : await ReadPurchaseAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SupplierPurchaseDto> IgnoreSupplierPurchaseAsync(
        Guid id,
        bool ignored,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var purchase = await LoadPurchaseForWriteAsync(id, cancellationToken);
        if (purchase.OrderId is not null)
        {
            throw new BusinessException("Đơn NCC đang gắn vào một đơn hàng — gỡ ra trước khi bỏ qua.");
        }

        purchase.MatchState = ignored
            ? SupplierPurchaseMatchStates.Ignored
            : SupplierPurchaseMatchStates.Unmatched;
        purchase.IsCharged = false;
        purchase.MatchedByUserId = ignored ? actingUserId : null;

        if (ignored)
        {
            // Luồng sync đọc SkipReason để báo cáo lý do bỏ qua, nên dòng bị bỏ qua bằng tay mà không
            // có lý do nào trước đó phải mang mã riêng thay vì null.
            purchase.SkipReason ??= SheinSkipReasons.OperatorIgnored;
        }
        else
        {
            // Trả về hàng đợi = mở lại cho luồng khớp. Lý do cũ (kể cả `ledger_deleted`) giờ là quá
            // khứ; giữ lại thì hàng đợi hiển thị một quyết định đã bị rút, và lượt sync kế tiếp lại
            // báo cáo đúng mã đó dù nó không còn chặn gì.
            purchase.SkipReason = null;
            purchase.SkipEvidenceJson = null;
        }

        purchase.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return await ReadPurchaseAsync(id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteSupplierPurchaseAsync(
        Guid id,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var purchase = await LoadPurchaseForWriteAsync(id, cancellationToken);
        var now = DateTime.UtcNow;
        var order = await LoadOrderGraphAsync(purchase.OrderId, cancellationToken);

        // FK CostUpdate → purchase chỉ SetNull: nếu không xoá dòng chi thì sổ cái còn tiền ảo sau
        // khi lần mua đã biến mất.
        var costRows = await _db.FulfilTransactions
            .Where(t =>
                t.SupplierPurchaseId == purchase.Id
                && t.Source == FulfilTransaction.SourceCostUpdate)
            .ToListAsync(cancellationToken);
        if (costRows.Count > 0)
        {
            _db.FulfilTransactions.RemoveRange(costRows);
        }

        if (order is not null)
        {
            order.SupplierPurchases?.Remove(purchase);
        }

        _db.SupplierPurchases.Remove(purchase);

        if (order is not null)
        {
            OrderSupplierCost.ApplyAggregates(order);
            SyncFulfilAttributionWithPurchases(order);
            await ReconcileCostUpdateTransactionsAsync(order, actingUserId, null, cancellationToken);
            order.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> BulkDeleteSupplierPurchasesAsync(
        IReadOnlyList<Guid> ids,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một lần mua để xóa.");
        }

        if (distinctIds.Count > 200)
        {
            throw new BusinessException("Mỗi lần xóa tối đa 200 lần mua.");
        }

        var purchases = await _db.SupplierPurchases
            .Where(p => distinctIds.Contains(p.Id))
            .ToListAsync(cancellationToken);
        if (purchases.Count == 0)
        {
            throw new NotFoundException("Không tìm thấy đơn NCC.");
        }

        var purchaseIds = purchases.Select(p => p.Id).ToList();
        var now = DateTime.UtcNow;

        // FK CostUpdate → purchase chỉ SetNull: phải xoá dòng chi trước khi xoá lần mua, bằng không
        // sổ cái còn tiền ảo sau khi hàng đợi đã sạch.
        var costRows = await _db.FulfilTransactions
            .Where(t =>
                t.SupplierPurchaseId != null
                && purchaseIds.Contains(t.SupplierPurchaseId.Value)
                && t.Source == FulfilTransaction.SourceCostUpdate)
            .ToListAsync(cancellationToken);
        if (costRows.Count > 0)
        {
            _db.FulfilTransactions.RemoveRange(costRows);
        }

        var orderIds = purchases
            .Where(p => p.OrderId.HasValue)
            .Select(p => p.OrderId!.Value)
            .Distinct()
            .ToList();
        var orders = orderIds.Count == 0
            ? new List<Order>()
            : await _db.Orders
                .Include(o => o.SupplierPurchases)
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        foreach (var purchase in purchases)
        {
            if (purchase.OrderId is { } oid && ordersById.TryGetValue(oid, out var order))
            {
                order.SupplierPurchases?.Remove(purchase);
            }

            _db.SupplierPurchases.Remove(purchase);
        }

        // Mỗi đơn bị ảnh hưởng chỉ reconcile một lần — nhiều lần mua cùng đơn không cần dựng lại sổ
        // cái nhiều vòng.
        foreach (var order in orders)
        {
            OrderSupplierCost.ApplyAggregates(order);
            SyncFulfilAttributionWithPurchases(order);
            await ReconcileCostUpdateTransactionsAsync(order, actingUserId, null, cancellationToken);
            order.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return purchases.Count;
    }

    /// <inheritdoc />
    public async Task<int> RebuildCostUpdateLedgerAsync(
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        // Đơn có lần mua được tính tiền nhưng thiếu dòng chi tương ứng. Nguyên nhân là nhánh cũ bỏ
        // qua lần mua không quy kết được tài khoản/thẻ: đơn hiện giá mua trong khi sổ cái trống, và
        // vì `HasSheinIncrementalDelta` không thấy delta nào nên không lượt sync nào tự sửa lại.
        var orderIds = await _db.Orders
            .AsNoTracking()
            .Where(o => o.SupplierPurchases!.Any(p =>
                    p.IsCharged
                    && (p.SupplierTotal != null || (p.SupplierWalletPrice ?? 0m) > 0m))
                && !_db.FulfilTransactions.Any(t =>
                    t.OrderId == o.Id && t.Source == FulfilTransaction.SourceCostUpdate))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        if (orderIds.Count == 0) return 0;

        var rebuilt = 0;
        // Theo lô: một lượt duy nhất trên vài trăm đơn giữ cả graph trong change tracker và làm
        // SaveChanges thành một transaction khổng lồ — vỡ một dòng là mất cả đợt.
        foreach (var chunk in orderIds.Chunk(100))
        {
            var orders = await _db.Orders
                .Include(o => o.SupplierPurchases)
                .Where(o => chunk.Contains(o.Id))
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                if (await ReconcileCostUpdateTransactionsAsync(order, actingUserId, null, cancellationToken))
                {
                    rebuilt++;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return rebuilt;
    }

    private static IQueryable<SupplierPurchase> ApplySupplierPurchaseFilters(
        IQueryable<SupplierPurchase> q,
        SupplierPurchaseQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.MatchState))
        {
            var state = query.MatchState.Trim().ToLowerInvariant();
            q = q.Where(p => p.MatchState == state);
        }

        if (!string.IsNullOrWhiteSpace(query.SkipReason))
        {
            var reason = query.SkipReason.Trim().ToLowerInvariant();
            q = q.Where(p => p.SkipReason == reason);
        }

        if (query.FulfilAccountId.HasValue)
        {
            q = q.Where(p => p.FulfilAccountId == query.FulfilAccountId.Value);
        }

        if (query.OrderId.HasValue)
        {
            q = q.Where(p => p.OrderId == query.OrderId.Value);
        }

        if (query.PaidByUserId.HasValue)
        {
            var paidBy = query.PaidByUserId.Value;
            q = q.Where(p => (p.PaidByUserId ?? (p.Order != null ? p.Order.PaidByUserId : null)) == paidBy);
        }

        // Lọc theo mốc mua thật, lùi về mốc đặt rồi mốc ghi nhận: một dòng chưa lấy được clock nào
        // vẫn phải lọt vào khoảng ngày, bằng không nó vô hình với mọi bộ lọc thời gian.
        if (query.From.HasValue)
        {
            var from = query.From.Value;
            q = q.Where(p => (p.SupplierPaidAt ?? p.OrderedAt ?? p.FirstSeenAt) >= from);
        }

        if (query.To.HasValue)
        {
            var to = query.To.Value;
            q = q.Where(p => (p.SupplierPaidAt ?? p.OrderedAt ?? p.FirstSeenAt) <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var search = query.Keyword.Trim();
            q = q.Where(p =>
                (p.SupplierOrderNo != null && p.SupplierOrderNo.Contains(search))
                || (p.RelationBillno != null && p.RelationBillno.Contains(search))
                || (p.TrackingNo != null && p.TrackingNo.Contains(search))
                || (p.ReceiverName != null && p.ReceiverName.Contains(search))
                || (p.ReceiverPhone != null && p.ReceiverPhone.Contains(search))
                || (p.ReceiverPostalCode != null && p.ReceiverPostalCode.Contains(search)));
        }

        return q;
    }

    private async Task<SupplierPurchase> LoadPurchaseForWriteAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.SupplierPurchases.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException("Không tìm thấy đơn NCC.");

    private async Task<Order?> LoadOrderGraphAsync(Guid? orderId, CancellationToken cancellationToken) =>
        orderId is null
            ? null
            : await _db.Orders
                .Include(o => o.SupplierPurchases)
                .FirstOrDefaultAsync(o => o.Id == orderId.Value, cancellationToken);

    private async Task<SupplierPurchaseDto> ReadPurchaseAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(PurchaseProjection)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Không tìm thấy đơn NCC.");

    /// <summary>Một định nghĩa chiếu duy nhất, để danh sách và thao tác đơn lẻ không trả khác nhau.</summary>
    private static readonly Expression<Func<SupplierPurchase, SupplierPurchaseDto>> PurchaseProjection =
        p => new SupplierPurchaseDto
        {
            Id = p.Id,
            OrderId = p.OrderId,
            PlatformOrderId = p.Order != null ? p.Order.PlatformOrderId : null,
            FourSellerOrderId = p.Order != null ? p.Order.FourSellerOrderId : null,
            ShopName = p.Order != null && p.Order.Shop != null ? p.Order.Shop.Name : null,
            SupplierOrderNo = p.SupplierOrderNo,
            SupplierOrderUrl = p.SupplierOrderUrl,
            Source = p.Source,
            MatchState = p.MatchState,
            SkipReason = p.SkipReason,
            SkipEvidenceJson = p.SkipEvidenceJson,
            IsCharged = p.IsCharged,
            SupplierTotal = p.SupplierTotal,
            SupplierSubtotal = p.SupplierSubtotal,
            Currency = p.Currency,
            OrderedAt = p.OrderedAt,
            SupplierPaidAt = p.SupplierPaidAt,
            SupplierUnpaid = p.SupplierUnpaid,
            IsMembershipOrVirtual = p.IsMembershipOrVirtual,
            SupplierStatus = p.SupplierStatus,
            TrackingNo = p.TrackingNo,
            WarehouseTrackingNo = p.WarehouseTrackingNo,
            WarehouseTrackingSeenAt = p.WarehouseTrackingSeenAt,
            TrackingKind = p.TrackingKind,
            Carrier = p.Carrier,
            ReceiverName = p.ReceiverName,
            ReceiverPhone = p.ReceiverPhone,
            ReceiverPostalCode = p.ReceiverPostalCode,
            FulfilAccountId = p.FulfilAccountId,
            FulfilAccountName = p.FulfilAccount != null ? p.FulfilAccount.Name : null,
            PaidByUserId = p.PaidByUserId ?? (p.Order != null ? p.Order.PaidByUserId : null),
            PaidByUserName = p.PaidByUser != null
                ? (string.IsNullOrWhiteSpace(p.PaidByUser.FullName) ? p.PaidByUser.UserName : p.PaidByUser.FullName)
                : (p.Order != null && p.Order.PaidByUser != null
                    ? (string.IsNullOrWhiteSpace(p.Order.PaidByUser.FullName)
                        ? p.Order.PaidByUser.UserName
                        : p.Order.PaidByUser.FullName)
                    : null),
            FirstSeenAt = p.FirstSeenAt,
            LastSeenAt = p.LastSeenAt,
            LastRunId = p.LastRunId,
            MatchedAt = p.MatchedAt,
            ManualLinkedAt = p.ManualLinkedAt
        };
}
