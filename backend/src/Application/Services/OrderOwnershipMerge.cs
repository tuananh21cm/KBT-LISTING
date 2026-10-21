using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// Merges two local order rows that share the same marketplace <see cref="Order.PlatformOrderId"/>
/// (typical after shop transfer + sync under the new seller).
/// </summary>
public static class OrderOwnershipMerge
{
    /// <summary>
    /// Picks which row to keep: prefer <paramref name="preferredOwnerId"/>, else richer fulfil/tracking data.
    /// </summary>
    public static Order ChooseKeep(Order a, Order b, Guid preferredOwnerId)
    {
        if (a.OwnerId == preferredOwnerId && b.OwnerId != preferredOwnerId)
        {
            return a;
        }

        if (b.OwnerId == preferredOwnerId && a.OwnerId != preferredOwnerId)
        {
            return b;
        }

        var scoreA = RichnessScore(a);
        var scoreB = RichnessScore(b);
        if (scoreA != scoreB)
        {
            return scoreA >= scoreB ? a : b;
        }

        var timeA = a.UpdatedAt ?? a.LastSyncedAt;
        var timeB = b.UpdatedAt ?? b.LastSyncedAt;
        return timeA >= timeB ? a : b;
    }

    /// <summary>
    /// Moves purchases / fulfil ledger / missing fields from <paramref name="drop"/> onto
    /// <paramref name="keep"/>, sets keep owner/shop, then deletes <paramref name="drop"/>.
    /// Caller persists.
    /// </summary>
    public static async Task MergeIntoAsync(
        IApplicationDbContext db,
        Order keep,
        Order drop,
        Guid preferredOwnerId,
        Guid? preferredShopId,
        long? preferredFourSellerOrderId,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        if (keep.Id == drop.Id)
        {
            return;
        }

        var now = DateTime.UtcNow;

        await MoveSupplierPurchasesAsync(db, keep.Id, drop.Id, cancellationToken);
        await MoveFulfilTransactionsAsync(db, keep.Id, drop.Id, cancellationToken);
        await MoveOrderIssueTagsAsync(db, keep.Id, drop.Id, cancellationToken);
        await MoveFulfilAttemptsAsync(db, keep.Id, drop.Id, cancellationToken);
        await RetargetReceivingBankTransactionsAsync(db, keep.Id, drop.Id, cancellationToken);
        await RetargetUspsScanItemsAsync(db, keep.Id, drop.Id, cancellationToken);
        CopyMissingOperationalFields(keep, drop);

        keep.OwnerId = preferredOwnerId;
        if (preferredShopId.HasValue)
        {
            keep.ShopId = preferredShopId;
        }
        else if (keep.ShopId is null && drop.ShopId is not null)
        {
            keep.ShopId = drop.ShopId;
        }

        if (preferredFourSellerOrderId is > 0
            && keep.FourSellerOrderId != preferredFourSellerOrderId.Value)
        {
            var conflict = await db.Orders
                .AsNoTracking()
                .AnyAsync(
                    o => o.FourSellerOrderId == preferredFourSellerOrderId.Value && o.Id != keep.Id && o.Id != drop.Id,
                    cancellationToken);
            if (!conflict)
            {
                keep.FourSellerOrderId = preferredFourSellerOrderId.Value;
            }
        }

        if (keep.FourSellerShopId is null or 0 && drop.FourSellerShopId is > 0)
        {
            keep.FourSellerShopId = drop.FourSellerShopId;
        }

        keep.UpdatedAt = now;

        if (drop.Items.Count > 0)
        {
            db.OrderItems.RemoveRange(drop.Items);
            drop.Items.Clear();
        }

        db.Orders.Remove(drop);

        logger?.LogInformation(
            "Merged duplicate order PlatformOrderId={PlatformOrderId}: keep={KeepId} drop={DropId} owner={OwnerId}",
            keep.PlatformOrderId,
            keep.Id,
            drop.Id,
            preferredOwnerId);
    }

    private static int RichnessScore(Order o)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(o.TrackingNo)) score += 8;
        if (!string.IsNullOrWhiteSpace(o.ShippingService)) score += 2;
        if (o.FulfilPurchasedAt.HasValue) score += 4;
        if (o.HasCarrierTracking) score += 2;
        if (o.HasSupplierPurchase) score += 2;
        if (o.DeliveredAt.HasValue) score += 2;
        if (o.SupplierPurchases?.Count > 0) score += Math.Min(o.SupplierPurchases.Count, 3);
        return score;
    }

    private static void CopyMissingOperationalFields(Order keep, Order drop)
    {
        if (string.IsNullOrWhiteSpace(keep.TrackingNo) && !string.IsNullOrWhiteSpace(drop.TrackingNo))
        {
            keep.TrackingNo = drop.TrackingNo;
        }

        if (string.IsNullOrWhiteSpace(keep.ShippingService) && !string.IsNullOrWhiteSpace(drop.ShippingService))
        {
            keep.ShippingService = drop.ShippingService;
        }

        if (string.IsNullOrWhiteSpace(keep.ShippingMethod) && !string.IsNullOrWhiteSpace(drop.ShippingMethod))
        {
            keep.ShippingMethod = drop.ShippingMethod;
        }

        if (string.IsNullOrWhiteSpace(keep.ShipmentNo) && !string.IsNullOrWhiteSpace(drop.ShipmentNo))
        {
            keep.ShipmentNo = drop.ShipmentNo;
        }

        keep.FulfilPurchasedAt ??= drop.FulfilPurchasedAt;
        keep.FulfilAccountId ??= drop.FulfilAccountId;
        keep.FulfilCardId ??= drop.FulfilCardId;
        keep.PaidByUserId ??= drop.PaidByUserId;
        keep.DeliveredAt ??= drop.DeliveredAt;
        keep.HasCarrierTracking = keep.HasCarrierTracking || drop.HasCarrierTracking;
        keep.HasSupplierPurchase = keep.HasSupplierPurchase || drop.HasSupplierPurchase;
        keep.HasUspsSignal = keep.HasUspsSignal || drop.HasUspsSignal;
        keep.HasWarehouseSignal = keep.HasWarehouseSignal || drop.HasWarehouseSignal;
        keep.IsSettlementPaid = keep.IsSettlementPaid || drop.IsSettlementPaid;
        keep.TikTokPaymentId ??= drop.TikTokPaymentId;

        if (string.IsNullOrWhiteSpace(keep.Message) && !string.IsNullOrWhiteSpace(drop.Message))
        {
            keep.Message = drop.Message;
        }

        // The tags themselves already moved onto keep; leaving the denorm at None drops the order out
        // of the issue queue on /orders even though it still carries the tags.
        if (IsNoIssue(keep.IssueStatus) && !IsNoIssue(drop.IssueStatus))
        {
            keep.IssueStatus = drop.IssueStatus;
        }
    }

    private static bool IsNoIssue(string? status)
        => string.IsNullOrWhiteSpace(status)
           || string.Equals(status, OrderIssueStatuses.None, StringComparison.OrdinalIgnoreCase);

    private static async Task MoveSupplierPurchasesAsync(
        IApplicationDbContext db,
        Guid keepOrderId,
        Guid dropOrderId,
        CancellationToken cancellationToken)
    {
        var dropPurchases = await db.SupplierPurchases
            .Where(p => p.OrderId == dropOrderId)
            .ToListAsync(cancellationToken);
        if (dropPurchases.Count == 0)
        {
            return;
        }

        var keepBillnos = await db.SupplierPurchases
            .AsNoTracking()
            .Where(p => p.OrderId == keepOrderId && p.SupplierOrderNo != null && p.SupplierOrderNo != string.Empty)
            .Select(p => p.SupplierOrderNo!)
            .ToListAsync(cancellationToken);
        var keepBillnoSet = keepBillnos.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var purchase in dropPurchases)
        {
            if (!string.IsNullOrWhiteSpace(purchase.SupplierOrderNo)
                && keepBillnoSet.Contains(purchase.SupplierOrderNo))
            {
                // Duplicate billno on keep — detach so delete of drop order SetNulls cleanly.
                purchase.OrderId = null;
                purchase.UpdatedAt = DateTime.UtcNow;
                continue;
            }

            purchase.OrderId = keepOrderId;
            purchase.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(purchase.SupplierOrderNo))
            {
                keepBillnoSet.Add(purchase.SupplierOrderNo);
            }
        }
    }

    private static async Task MoveFulfilTransactionsAsync(
        IApplicationDbContext db,
        Guid keepOrderId,
        Guid dropOrderId,
        CancellationToken cancellationToken)
    {
        var rows = await db.FulfilTransactions
            .Where(t => t.OrderId == dropOrderId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OrderId = keepOrderId;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static async Task MoveOrderIssueTagsAsync(
        IApplicationDbContext db,
        Guid keepOrderId,
        Guid dropOrderId,
        CancellationToken cancellationToken)
    {
        var dropTags = await db.OrderIssueTags
            .Where(t => t.OrderId == dropOrderId)
            .ToListAsync(cancellationToken);
        if (dropTags.Count == 0)
        {
            return;
        }

        var keepTagIds = await db.OrderIssueTags
            .AsNoTracking()
            .Where(t => t.OrderId == keepOrderId)
            .Select(t => t.IssueTagId)
            .ToListAsync(cancellationToken);
        var keepSet = keepTagIds.ToHashSet();

        // Rows added by an earlier merge into the same keep are not in the store yet, so the
        // query above cannot see them. Missing them means Add() on a key already tracked.
        foreach (var entry in db.ChangeTracker.Entries<OrderIssueTag>())
        {
            if (entry.State != EntityState.Deleted && entry.Entity.OrderId == keepOrderId)
            {
                keepSet.Add(entry.Entity.IssueTagId);
            }
        }

        // (OrderId, IssueTagId) is the primary key, so the row cannot be repointed in place:
        // drop the old row and insert the keep-side twin.
        foreach (var tag in dropTags)
        {
            db.OrderIssueTags.Remove(tag);
            if (!keepSet.Add(tag.IssueTagId))
            {
                continue;
            }

            db.OrderIssueTags.Add(new OrderIssueTag
            {
                OrderId = keepOrderId,
                IssueTagId = tag.IssueTagId,
                AssignedByUserId = tag.AssignedByUserId,
                AssignedAt = tag.AssignedAt
            });
        }
    }

    /// <summary>
    /// Attempts cascade-delete with the dropped order, so the purchase / claim history has to follow
    /// the kept row. An open attempt stays behind when its session already has one on keep:
    /// <c>(OrderId, SessionId) WHERE ClosedAt IS NULL</c> is unique.
    /// </summary>
    private static async Task MoveFulfilAttemptsAsync(
        IApplicationDbContext db,
        Guid keepOrderId,
        Guid dropOrderId,
        CancellationToken cancellationToken)
    {
        var rows = await db.OrderFulfilAttempts
            .Where(a => a.OrderId == dropOrderId)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return;
        }

        var openSessions = (await db.OrderFulfilAttempts
                .AsNoTracking()
                .Where(a => a.OrderId == keepOrderId && a.ClosedAt == null)
                .Select(a => a.SessionId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            if (row.ClosedAt is null && !openSessions.Add(row.SessionId))
            {
                continue;
            }

            row.OrderId = keepOrderId;
            row.UpdatedAt = now;
        }
    }

    private static async Task RetargetReceivingBankTransactionsAsync(
        IApplicationDbContext db,
        Guid keepOrderId,
        Guid dropOrderId,
        CancellationToken cancellationToken)
    {
        var rows = await db.ReceivingBankTransactions
            .Where(t => t.OrderId == dropOrderId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OrderId = keepOrderId;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The FK is <c>SetNull</c>, so a scan line would survive the delete with no order behind it —
    /// the USPS list then shows a tracking number it can no longer name.
    /// </summary>
    private static async Task RetargetUspsScanItemsAsync(
        IApplicationDbContext db,
        Guid keepOrderId,
        Guid dropOrderId,
        CancellationToken cancellationToken)
    {
        var rows = await db.UspsScanBatchItems
            .Where(i => i.OrderId == dropOrderId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.OrderId = keepOrderId;
        }
    }
}
