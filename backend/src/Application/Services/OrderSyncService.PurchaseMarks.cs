using Application.DTOs.Order;
using Application.DTOs.Shein;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static Application.Services.SheinMatchKeys;

namespace Application.Services;

/// <summary>
/// Dấu «Đã mua» (OrderFulfilAttempt Outcome=Purchased / FulfilPurchasedAt Manual) làm bằng chứng
/// khớp billno↔đơn và suy người mua — mạnh hơn suy đoán tuần tự theo số chỗ trống.
/// </summary>
public partial class OrderSyncService
{
    /// <summary>Biên độ lệch đồng hồ giữa mốc bấm Đã mua và pay_time SHEIN (E1 bao trùm).</summary>
    internal static readonly TimeSpan PurchaseMarkSlack = TimeSpan.FromMinutes(30);

    /// <summary>Khoảng cách tối đa CompletedAt ↔ pay_time cho tầng E2 (gần nhất).</summary>
    internal static readonly TimeSpan PurchaseMarkMaxDistance = TimeSpan.FromHours(6);

    /// <summary>Cửa sổ CustomerKey (T3) quanh pay_time khi suy người mua.</summary>
    internal static readonly TimeSpan PurchaseBuyerCustomerKeyWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Snapshot attempt / mốc Manual prefetch một lần mỗi lượt sync — không query theo từng billno.
    /// </summary>
    internal sealed class PurchaseMarkIndex
    {
        private readonly Dictionary<Guid, List<PurchaseMarkAttempt>> _byOrderId;
        private readonly Dictionary<string, List<PurchaseMarkAttempt>> _byCustomerKey;
        private readonly HashSet<Guid> _consumedAttempts = new();

        public PurchaseMarkIndex(
            IReadOnlyList<PurchaseMarkAttempt> attempts)
        {
            _byOrderId = attempts
                .GroupBy(a => a.OrderId)
                .ToDictionary(g => g.Key, g => g.ToList());
            _byCustomerKey = attempts
                .Where(a => !string.IsNullOrEmpty(a.CustomerKey))
                .GroupBy(a => a.CustomerKey!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        }

        /// <summary>Empty index — lô đơn lẻ / đã đủ người mua, không query attempt.</summary>
        public static PurchaseMarkIndex Empty { get; } = new([]);

        public bool IsEmpty => _byOrderId.Count == 0;

        public bool IsConsumed(Guid attemptId) => _consumedAttempts.Contains(attemptId);

        public void Consume(Guid attemptId) => _consumedAttempts.Add(attemptId);

        public IReadOnlyList<PurchaseMarkAttempt> ForOrder(Guid orderId) =>
            _byOrderId.TryGetValue(orderId, out var list) ? list : Array.Empty<PurchaseMarkAttempt>();

        public IReadOnlyList<PurchaseMarkAttempt> ForCustomerKey(string? customerKey)
        {
            if (string.IsNullOrEmpty(customerKey)) return Array.Empty<PurchaseMarkAttempt>();
            return _byCustomerKey.TryGetValue(customerKey, out var list)
                ? list
                : Array.Empty<PurchaseMarkAttempt>();
        }
    }

    /// <summary>Projection scalar của một attempt (không Include navigation).</summary>
    internal sealed record PurchaseMarkAttempt(
        Guid Id,
        Guid OrderId,
        Guid UserId,
        Guid SessionId,
        DateTime StartedAt,
        DateTime? CompletedAt,
        string? CustomerKey,
        string? Outcome,
        bool IsOpen);

    /// <summary>
    /// Prefetch attempt Purchased + đang mở cho pool. Bỏ hẳn query khi mọi đơn đã có
    /// <c>PaidByUserId</c> và không có cặp phone|zip trùng (lô đơn lẻ).
    /// </summary>
    private async Task<PurchaseMarkIndex> BuildPurchaseMarkIndexAsync(
        IReadOnlyList<Order> pool,
        IReadOnlyList<SheinOrderDto> sheinOrders,
        CancellationToken cancellationToken)
    {
        if (pool.Count == 0) return PurchaseMarkIndex.Empty;

        var hasSiblingGroup = HasAnySiblingCustomerGroup(pool);
        var anyMissingBuyer = pool.Any(o => o.PaidByUserId is null);
        if (!hasSiblingGroup && !anyMissingBuyer)
        {
            return PurchaseMarkIndex.Empty;
        }

        var orderIds = pool.Select(o => o.Id).ToList();
        var customerKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in pool)
        {
            var key = CustomerKeyOf(order);
            if (key is not null) customerKeys.Add(key);
        }

        DateTime? minPayAt = null;
        foreach (var shein in sheinOrders)
        {
            var pay = shein.SheinPaidAt ?? shein.OrderedAt;
            if (pay is null) continue;
            var utc = NormalizeUtc(pay.Value);
            if (minPayAt is null || utc < minPayAt) minPayAt = utc;
        }

        var completedAfter = (minPayAt ?? DateTime.UtcNow) - PurchaseBuyerCustomerKeyWindow;

        var rows = await _db.OrderFulfilAttempts
            .AsNoTracking()
            .Where(a =>
                (a.ClosedAt == null
                    || (a.Outcome == FulfilAttemptOutcomes.Purchased
                        && a.CompletedAt != null
                        && a.CompletedAt >= completedAfter))
                && (orderIds.Contains(a.OrderId)
                    || (a.CustomerKey != null && customerKeys.Contains(a.CustomerKey))))
            .Select(a => new PurchaseMarkAttempt(
                a.Id,
                a.OrderId,
                a.UserId,
                a.SessionId,
                a.StartedAt,
                a.CompletedAt,
                a.CustomerKey,
                a.Outcome,
                a.ClosedAt == null))
            .ToListAsync(cancellationToken);

        return new PurchaseMarkIndex(rows);
    }

    private static bool HasAnySiblingCustomerGroup(IReadOnlyList<Order> pool)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in pool)
        {
            var key = CustomerKeyOf(order);
            if (key is null) continue;
            if (!seen.Add(key)) return true;
        }

        return false;
    }

    private static string? CustomerKeyOf(Order order)
    {
        var phone = PhoneKey(order.BuyerPhone);
        var zip = ZipKey(order.PostalCode);
        return phone.Length > 0 && zip.Length > 0 ? $"{phone}|{zip}" : null;
    }

    /// <summary>
    /// Chọn đơn trong <paramref name="candidates"/> theo dấu Đã mua (E1 bao trùm → E2 gần nhất).
    /// Chỉ xếp lại thứ tự trong tập đã lọc guard (capacity / causality / claimed) — không mở cửa.
    /// </summary>
    /// <returns>
    /// Đơn khớp + attempt đã dùng (null khi E2 chỉ dựa trên FulfilPurchasedAt Manual, không có attempt).
    /// </returns>
    internal static (Order? Order, PurchaseMarkAttempt? Attempt) FindOrderByPurchaseMark(
        IReadOnlyList<Order> candidates,
        SheinOrderDto shein,
        PurchaseMarkIndex index,
        IReadOnlyCollection<Guid>? claimedThisPass = null)
    {
        if (candidates.Count == 0) return (null, null);

        var payAtRaw = shein.SheinPaidAt ?? shein.OrderedAt;
        if (payAtRaw is null) return (null, null);
        var payAt = NormalizeUtc(payAtRaw.Value);

        // E1 — attempt Purchased bao trùm pay_time (± slack).
        PurchaseMarkAttempt? bestE1 = null;
        Order? bestE1Order = null;
        var bestE1Span = TimeSpan.MaxValue;

        foreach (var order in candidates)
        {
            if (claimedThisPass is not null && claimedThisPass.Contains(order.Id)) continue;

            foreach (var attempt in index.ForOrder(order.Id))
            {
                if (index.IsConsumed(attempt.Id)
                    && !SharesSheinShipmentGroupPending(order, shein))
                {
                    continue;
                }

                if (!string.Equals(attempt.Outcome, FulfilAttemptOutcomes.Purchased, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attempt.CompletedAt is null) continue;
                var started = NormalizeUtc(attempt.StartedAt) - PurchaseMarkSlack;
                var completed = NormalizeUtc(attempt.CompletedAt.Value) + PurchaseMarkSlack;
                if (payAt < started || payAt > completed) continue;

                var span = completed - started;
                if (bestE1 is null || span < bestE1Span)
                {
                    bestE1 = attempt;
                    bestE1Order = order;
                    bestE1Span = span;
                }
            }
        }

        if (bestE1Order is not null && bestE1 is not null)
        {
            return (bestE1Order, bestE1);
        }

        // E2 — gần nhất theo CompletedAt / FulfilPurchasedAt Manual, chỉ đơn đang trống lần mua.
        Order? bestE2Order = null;
        PurchaseMarkAttempt? bestE2Attempt = null;
        var bestE2Distance = TimeSpan.MaxValue;

        foreach (var order in candidates)
        {
            if (claimedThisPass is not null && claimedThisPass.Contains(order.Id)) continue;
            if (LivePurchaseCount(order) > 0) continue;

            foreach (var attempt in index.ForOrder(order.Id))
            {
                if (index.IsConsumed(attempt.Id)) continue;
                if (!string.Equals(attempt.Outcome, FulfilAttemptOutcomes.Purchased, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attempt.CompletedAt is null) continue;
                var distance = Abs(payAt - NormalizeUtc(attempt.CompletedAt.Value));
                if (distance > PurchaseMarkMaxDistance) continue;
                if (distance < bestE2Distance)
                {
                    bestE2Distance = distance;
                    bestE2Order = order;
                    bestE2Attempt = attempt;
                }
            }

            // Proxy: bấm từ web admin / dữ liệu trước bảng attempt — Manual stamp trên đơn.
            if (order.FulfilPurchasedAt is { } stamped
                && OrderFulfilTimeline.SourceOf(order, OrderFulfilTimeline.Milestone.FulfilPurchased)
                    == OrderFulfilTimeline.Source.Manual)
            {
                var distance = Abs(payAt - NormalizeUtc(stamped));
                if (distance > PurchaseMarkMaxDistance) continue;
                if (distance < bestE2Distance)
                {
                    bestE2Distance = distance;
                    bestE2Order = order;
                    bestE2Attempt = null;
                }
            }
        }

        return (bestE2Order, bestE2Attempt);
    }

    /// <summary>
    /// Billno khớp dấu Đã mua với chính đơn đang giữ — rebalance không được chuyển đi.
    /// Trong các lần mua live trên đơn, chỉ billno có <c>payAt</c> gần <c>CompletedAt</c> nhất
    /// (và ≤ slack) được miễn trừ — một attempt không che cả hai billno.
    /// </summary>
    internal static bool MatchesPurchaseMark(
        Order order,
        SupplierPurchase purchase,
        PurchaseMarkIndex? index)
    {
        if (index is null || index.IsEmpty) return false;
        var payAt = purchase.SupplierPaidAt ?? purchase.OrderedAt;
        if (payAt is null) return false;
        var pay = NormalizeUtc(payAt.Value);

        PurchaseMarkAttempt? bestAttempt = null;
        var bestDistance = TimeSpan.MaxValue;
        foreach (var attempt in index.ForOrder(order.Id))
        {
            if (!string.Equals(attempt.Outcome, FulfilAttemptOutcomes.Purchased, StringComparison.Ordinal))
            {
                continue;
            }

            if (attempt.CompletedAt is null) continue;
            var distance = Abs(pay - NormalizeUtc(attempt.CompletedAt.Value));
            if (distance > PurchaseMarkSlack) continue;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAttempt = attempt;
            }
        }

        if (bestAttempt?.CompletedAt is null) return false;

        // Chỉ đúng purchase này là gần CompletedAt nhất trên đơn — so với anh em live khác.
        var completed = NormalizeUtc(bestAttempt.CompletedAt.Value);
        SupplierPurchase? closest = null;
        var closestDistance = TimeSpan.MaxValue;
        foreach (var other in order.SupplierPurchases ?? [])
        {
            if (!IsLiveSupplierPurchase(other)) continue;
            var otherPay = other.SupplierPaidAt ?? other.OrderedAt;
            if (otherPay is null) continue;
            var d = Abs(NormalizeUtc(otherPay.Value) - completed);
            if (d < closestDistance)
            {
                closestDistance = d;
                closest = other;
            }
        }

        return closest is not null && closest.Id == purchase.Id;
    }

    /// <summary>
    /// Suy người mua cho lần mua vừa gắn: T1 order → T2 attempt đang mở → T3 CustomerKey ±24h → T4 null.
    /// T2 đóng attempt thành Purchased (mutation trong cùng SaveChanges của lượt sync).
    /// </summary>
    private Guid? ResolvePurchaseBuyer(
        Order order,
        SupplierPurchase purchase,
        PurchaseMarkIndex index,
        DateTime now)
    {
        if (order.PaidByUserId is { } existing) return existing;

        // T2 — attempt đang mở trên chính đơn.
        PurchaseMarkAttempt? openSnap = null;
        foreach (var attempt in index.ForOrder(order.Id))
        {
            if (!attempt.IsOpen) continue;
            if (index.IsConsumed(attempt.Id)) continue;
            if (openSnap is null || attempt.StartedAt < openSnap.StartedAt)
            {
                openSnap = attempt;
            }
        }

        if (openSnap is not null)
        {
            index.Consume(openSnap.Id);
            CloseOpenAttemptAsPurchased(openSnap, purchase, now);
            return openSnap.UserId;
        }

        // T3 — attempt Purchased gần nhất cùng CustomerKey trong ±24h quanh pay_time.
        var payAt = purchase.SupplierPaidAt ?? purchase.OrderedAt;
        if (payAt is null) return null;
        var pay = NormalizeUtc(payAt.Value);
        var customerKey = CustomerKeyOf(order);

        PurchaseMarkAttempt? best = null;
        var bestDistance = TimeSpan.MaxValue;
        foreach (var attempt in index.ForCustomerKey(customerKey))
        {
            if (!string.Equals(attempt.Outcome, FulfilAttemptOutcomes.Purchased, StringComparison.Ordinal))
            {
                continue;
            }

            if (attempt.CompletedAt is null) continue;
            var distance = Abs(pay - NormalizeUtc(attempt.CompletedAt.Value));
            if (distance > PurchaseBuyerCustomerKeyWindow) continue;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = attempt;
            }
        }

        return best?.UserId;
    }

    /// <summary>
    /// Đóng attempt đang mở (đã tracked hoặc load lại) thành Purchased khi sync xác nhận mua trước cú bấm.
    /// </summary>
    private void CloseOpenAttemptAsPurchased(
        PurchaseMarkAttempt snap,
        SupplierPurchase purchase,
        DateTime now)
    {
        var entity = _db.OrderFulfilAttempts.Local.FirstOrDefault(a => a.Id == snap.Id);
        if (entity is null)
        {
            // Prefetch AsNoTracking — gắn stub để CloseAttempt ghi đúng.
            entity = new OrderFulfilAttempt
            {
                Id = snap.Id,
                OrderId = snap.OrderId,
                UserId = snap.UserId,
                SessionId = snap.SessionId,
                StartedAt = snap.StartedAt,
                LastRenewedAt = snap.StartedAt,
                ExpiresAt = snap.StartedAt,
                CustomerKey = snap.CustomerKey,
                BatchId = Guid.Empty,
                CreatedAt = snap.StartedAt,
            };
            _db.OrderFulfilAttempts.Attach(entity);
        }

        if (entity.ClosedAt is not null) return;

        var completedAt = purchase.SupplierPaidAt ?? purchase.OrderedAt ?? now;
        CloseAttempt(entity, FulfilAttemptOutcomes.Purchased, now, completedAt: NormalizeUtc(completedAt));
        ClearLeaseFieldsIfHolder(entity.OrderId, entity.UserId);
    }

    private void ClearLeaseFieldsIfHolder(Guid orderId, Guid userId)
    {
        var order = _db.Orders.Local.FirstOrDefault(o => o.Id == orderId);
        if (order is null) return;
        if (order.FulfilClaimedByUserId != userId) return;

        var stillOpen = _db.OrderFulfilAttempts.Local.Any(a =>
            a.OrderId == orderId && a.ClosedAt == null);
        if (!stillOpen)
        {
            ClearLeaseFields(order);
        }
    }

    private static TimeSpan Abs(TimeSpan value) => value < TimeSpan.Zero ? -value : value;
}
