using Application.DTOs.Order;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using static Application.Services.SheinMatchKeys;

namespace Application.Services;

/// <summary>
/// Fulfil order claim / lease + per-session attempt history.
/// </summary>
public partial class OrderSyncService
{
    /// <inheritdoc />
    public async Task<FulfilClaimResultDto> ClaimAsync(
        Guid orderId,
        Guid sessionId,
        string? sessionLabel,
        IReadOnlyList<Guid>? siblingOrderIds,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var ttlMinutes = await GetClaimTtlMinutesAsync(cancellationToken);
        var maxPerUser = await GetClaimMaxPerUserAsync(cancellationToken);
        var newExpiresAt = now.AddMinutes(ttlMinutes);
        var batchId = Guid.NewGuid();

        await using var tx = await BeginDbTransactionAsync(cancellationToken);

        var primaryOrder = await _db.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new { o.Id, o.BuyerPhone, o.PostalCode })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        // One session = one focus group: release any other open attempts of this session
        // before claiming the new primary (+ siblings in this request).
        var keepIds = new HashSet<Guid> { orderId };
        if (siblingOrderIds is { Count: > 0 })
        {
            foreach (var sid in siblingOrderIds.Where(id => id != orderId))
            {
                keepIds.Add(sid);
            }
        }

        var releasedClaims = await ReleaseSessionFocusOutsideKeepAsync(
            sessionId,
            keepIds,
            now,
            cancellationToken);

        var primary = await ClaimOneOrderAsync(
            orderId,
            sessionId,
            sessionLabel,
            actingUserId,
            now,
            newExpiresAt,
            batchId,
            customerKey: null,
            checkQuota: true,
            maxPerUser,
            cancellationToken);

        var siblingClaims = new List<FulfilClaimItemDto>();
        var conflicts = new List<FulfilClaimConflictDto>();

        var phone = PhoneKey(primaryOrder.BuyerPhone);
        var zip = ZipKey(primaryOrder.PostalCode);
        string? customerKey = phone.Length > 0 && zip.Length > 0 ? $"{phone}|{zip}" : null;

        if (customerKey is not null && siblingOrderIds is { Count: > 0 })
        {
            var siblingIds = siblingOrderIds
                .Where(id => id != orderId)
                .Distinct()
                .ToList();

            if (siblingIds.Count > 0)
            {
                var siblings = await _db.Orders
                    .AsNoTracking()
                    .Where(o => siblingIds.Contains(o.Id))
                    .Select(o => new { o.Id, o.BuyerPhone, o.PostalCode })
                    .ToListAsync(cancellationToken);

                foreach (var sib in siblings)
                {
                    if (PhoneKey(sib.BuyerPhone) != phone || ZipKey(sib.PostalCode) != zip)
                    {
                        continue;
                    }

                    try
                    {
                        var item = await ClaimOneOrderAsync(
                            sib.Id,
                            sessionId,
                            sessionLabel,
                            actingUserId,
                            now,
                            newExpiresAt,
                            batchId,
                            customerKey,
                            checkQuota: true,
                            maxPerUser,
                            cancellationToken);
                        siblingClaims.Add(item);
                    }
                    catch (ConflictException ex)
                    {
                        conflicts.Add(new FulfilClaimConflictDto
                        {
                            OrderId = sib.Id,
                            Message = ex.Message,
                        });
                    }
                }
            }
        }

        // Stamp customer key on the primary attempt when known.
        if (customerKey is not null)
        {
            var primaryAttempt = await _db.OrderFulfilAttempts
                .FirstOrDefaultAsync(
                    a => a.OrderId == orderId
                        && a.SessionId == sessionId
                        && a.ClosedAt == null,
                    cancellationToken);
            if (primaryAttempt is not null)
            {
                primaryAttempt.CustomerKey = customerKey;
                primaryAttempt.UpdatedAt = now;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        await tx.CommitAsync(cancellationToken);

        var items = new List<FulfilClaimItemDto>();
        items.AddRange(releasedClaims);
        items.Add(primary);
        items.AddRange(siblingClaims);
        await _fulfilQueue.PublishClaimChangedAsync(items, now, cancellationToken);

        return new FulfilClaimResultDto
        {
            Claim = primary,
            SiblingClaims = siblingClaims,
            Conflicts = conflicts,
            ReleasedClaims = releasedClaims,
            ServerTime = now,
        };
    }

    /// <inheritdoc />
    public async Task<FulfilClaimResultDto> ReleaseClaimAsync(
        Guid orderId,
        Guid? sessionId,
        Guid actingUserId,
        bool isAdmin,
        bool allSessions,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var tx = await BeginDbTransactionAsync(cancellationToken);

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        var leaseActive = order.FulfilClaimExpiresAt is { } exp
            && exp > now
            && order.FulfilPurchasedAt is null
            && order.FulfilClaimedByUserId is not null;

        if (!leaseActive)
        {
            ClearLeaseFields(order);
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var free = BuildFreeClaimItem(orderId, now);
            await _fulfilQueue.PublishClaimChangedAsync([free], now, cancellationToken);
            return new FulfilClaimResultDto { Claim = free, ServerTime = now };
        }

        var holderId = order.FulfilClaimedByUserId!.Value;
        if (holderId != actingUserId && !isAdmin)
        {
            throw new UnauthorizedAccessException("Chỉ người đang giữ đơn hoặc Admin mới được nhả.");
        }

        if (allSessions || sessionId is null)
        {
            if (!isAdmin && holderId != actingUserId)
            {
                throw new UnauthorizedAccessException("Chỉ chủ lease hoặc Admin mới nhả toàn bộ phiên.");
            }

            await CloseOpenAttemptsAsync(
                orderId,
                holderId,
                sessionIdFilter: null,
                FulfilAttemptOutcomes.Released,
                now,
                cancellationToken);

            ClearLeaseFields(order);
            order.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            var released = BuildFreeClaimItem(orderId, now);
            await _fulfilQueue.PublishClaimChangedAsync([released], now, cancellationToken);
            return new FulfilClaimResultDto { Claim = released, ServerTime = now };
        }

        await CloseOpenAttemptsAsync(
            orderId,
            holderId,
            sessionId,
            FulfilAttemptOutcomes.Released,
            now,
            cancellationToken);
        // Persist closes before recount so open-session queries see ClosedAt.
        await _db.SaveChangesAsync(cancellationToken);

        var remaining = await RecountAndApplyLeaseAsync(order, holderId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var item = remaining;
        await _fulfilQueue.PublishClaimChangedAsync([item], now, cancellationToken);
        return new FulfilClaimResultDto { Claim = item, ServerTime = now };
    }

    /// <inheritdoc />
    public async Task<FulfilActiveClaimsDto> ReleaseSessionClaimsAsync(
        Guid sessionId,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var tx = await BeginDbTransactionAsync(cancellationToken);

        var items = await ReleaseSessionFocusOutsideKeepAsync(
            sessionId,
            keepOrderIds: [],
            now,
            cancellationToken,
            userIdFilter: actingUserId);

        await tx.CommitAsync(cancellationToken);

        if (items.Count > 0)
        {
            await _fulfilQueue.PublishClaimChangedAsync(items, now, cancellationToken);
        }

        return new FulfilActiveClaimsDto { Items = items, ServerTime = now };
    }

    /// <inheritdoc />
    public async Task<FulfilActiveClaimsDto> ListActiveClaimsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Dọn focus cũ: mỗi session chỉ giữ 1 batch (primary+siblings) mới nhất.
        await EnforceSingleBatchFocusPerSessionAsync(now, cancellationToken);

        var held = await _db.Orders
            .AsNoTracking()
            .Where(o =>
                o.FulfilPurchasedAt == null
                && o.FulfilClaimedByUserId != null
                && o.FulfilClaimExpiresAt != null
                && o.FulfilClaimExpiresAt > now)
            .Select(o => new
            {
                o.Id,
                o.FulfilClaimedByUserId,
                ClaimedByName = o.FulfilClaimedByUser!.FullName != null
                    && o.FulfilClaimedByUser.FullName != ""
                    ? o.FulfilClaimedByUser.FullName
                    : o.FulfilClaimedByUser.UserName,
                o.FulfilClaimedAt,
                o.FulfilClaimExpiresAt,
                o.FulfilClaimSessionCount,
            })
            .ToListAsync(cancellationToken);

        if (held.Count == 0)
        {
            return new FulfilActiveClaimsDto { ServerTime = now };
        }

        var orderIds = held.Select(h => h.Id).ToList();
        var openAttempts = await _db.OrderFulfilAttempts
            .AsNoTracking()
            .Where(a => orderIds.Contains(a.OrderId) && a.ClosedAt == null)
            .Select(a => new { a.OrderId, a.SessionId, a.StartedAt })
            .ToListAsync(cancellationToken);

        var byOrder = openAttempts.GroupBy(a => a.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = held.Select(h =>
        {
            byOrder.TryGetValue(h.Id, out var sessions);
            sessions ??= [];
            return new FulfilClaimItemDto
            {
                OrderId = h.Id,
                State = FulfilClaimStates.Held,
                ClaimedByUserId = h.FulfilClaimedByUserId,
                ClaimedByName = h.ClaimedByName,
                ClaimedAt = h.FulfilClaimedAt,
                ExpiresAt = h.FulfilClaimExpiresAt,
                SessionCount = sessions.Count > 0 ? sessions.Count : h.FulfilClaimSessionCount,
                SessionIds = sessions.Select(s => s.SessionId).ToList(),
                StartedAt = sessions.Count > 0
                    ? sessions.Min(s => s.StartedAt)
                    : h.FulfilClaimedAt,
            };
        }).ToList();

        return new FulfilActiveClaimsDto { Items = items, ServerTime = now };
    }

    /// <inheritdoc />
    public async Task<FulfilAttemptListPageDto> ListAttemptsAsync(
        FulfilAttemptListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = query.PageCurrent < 1 ? 1 : query.PageCurrent;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);

        var q = _db.OrderFulfilAttempts.AsNoTracking().AsQueryable();
        if (query.OrderId is { } oid)
        {
            q = q.Where(a => a.OrderId == oid);
        }

        if (query.UserId is { } uid)
        {
            q = q.Where(a => a.UserId == uid);
        }

        if (query.SessionId is { } sid)
        {
            q = q.Where(a => a.SessionId == sid);
        }

        if (query.DateFrom is { } from)
        {
            q = q.Where(a => a.StartedAt >= from);
        }

        if (query.DateTo is { } to)
        {
            q = q.Where(a => a.StartedAt <= to);
        }

        var total = await q.CountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(a => a.StartedAt)
            .ThenBy(a => a.Id)
            .Skip((pageCurrent - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new FulfilAttemptDto
            {
                Id = a.Id,
                OrderId = a.OrderId,
                PlatformOrderId = a.Order!.PlatformOrderId,
                UserId = a.UserId,
                UserName = a.User!.FullName != null && a.User.FullName != ""
                    ? a.User.FullName
                    : a.User.UserName,
                SessionId = a.SessionId,
                SessionLabel = a.SessionLabel,
                BatchId = a.BatchId,
                CustomerKey = a.CustomerKey,
                StartedAt = a.StartedAt,
                LastRenewedAt = a.LastRenewedAt,
                ExpiresAt = a.ExpiresAt,
                ClosedAt = a.ClosedAt,
                Outcome = a.Outcome,
                CompletedAt = a.CompletedAt,
                DurationSeconds = a.DurationSeconds,
            })
            .ToListAsync(cancellationToken);

        return new FulfilAttemptListPageDto
        {
            Items = rows,
            Total = total,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc />
    public async Task<int> CloseExpiredAttemptsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        await using var tx = await BeginDbTransactionAsync(cancellationToken);

        var expired = await _db.OrderFulfilAttempts
            .Where(a => a.ClosedAt == null && a.ExpiresAt <= now)
            .Select(a => new { a.Id, a.OrderId, a.UserId, a.StartedAt })
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return 0;
        }

        foreach (var row in expired)
        {
            var attempt = await _db.OrderFulfilAttempts
                .FirstOrDefaultAsync(a => a.Id == row.Id && a.ClosedAt == null, cancellationToken);
            if (attempt is null)
            {
                continue;
            }

            CloseAttempt(attempt, FulfilAttemptOutcomes.Expired, now, completedAt: null);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var affectedOrderIds = expired.Select(e => e.OrderId).Distinct().ToList();
        var publishItems = new List<FulfilClaimItemDto>();

        foreach (var oid in affectedOrderIds)
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == oid, cancellationToken);
            if (order is null)
            {
                continue;
            }

            if (order.FulfilPurchasedAt is not null || order.FulfilClaimedByUserId is null)
            {
                continue;
            }

            var item = await RecountAndApplyLeaseAsync(
                order,
                order.FulfilClaimedByUserId.Value,
                now,
                cancellationToken);
            publishItems.Add(item);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (publishItems.Count > 0)
        {
            await _fulfilQueue.PublishClaimChangedAsync(publishItems, now, cancellationToken);
        }

        return expired.Count;
    }

    /// <summary>
    /// Closes open attempts and clears lease when purchase is stamped (or force-taken).
    /// Called from <see cref="UpdateMilestonesAsync"/>.
    /// </summary>
    private async Task FinalizePurchaseAttemptsAsync(
        Order order,
        Guid actingUserId,
        Guid? sessionId,
        DateTime purchasedAt,
        DateTime now,
        bool forceTakeover,
        CancellationToken cancellationToken)
    {
        var open = await _db.OrderFulfilAttempts
            .Where(a => a.OrderId == order.Id && a.ClosedAt == null)
            .ToListAsync(cancellationToken);

        if (forceTakeover)
        {
            foreach (var a in open.Where(a => a.UserId != actingUserId))
            {
                CloseAttempt(a, FulfilAttemptOutcomes.TakenOver, now, completedAt: null);
            }

            open = open.Where(a => a.UserId == actingUserId).ToList();
        }

        OrderFulfilAttempt? purchasedAttempt = null;
        if (sessionId is { } sid)
        {
            purchasedAttempt = open.FirstOrDefault(a => a.SessionId == sid && a.UserId == actingUserId);
        }

        purchasedAttempt ??= open
            .Where(a => a.UserId == actingUserId)
            .OrderBy(a => a.StartedAt)
            .FirstOrDefault();

        if (purchasedAttempt is null)
        {
            // Stamp without a prior claim (web / SHEIN sync) — history row with null duration.
            _db.OrderFulfilAttempts.Add(new OrderFulfilAttempt
            {
                OrderId = order.Id,
                UserId = actingUserId,
                SessionId = sessionId ?? Guid.Empty,
                BatchId = Guid.NewGuid(),
                StartedAt = purchasedAt,
                LastRenewedAt = purchasedAt,
                ExpiresAt = purchasedAt,
                ClosedAt = now,
                Outcome = FulfilAttemptOutcomes.Purchased,
                CompletedAt = purchasedAt,
                DurationSeconds = null,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            CloseAttempt(
                purchasedAttempt,
                FulfilAttemptOutcomes.Purchased,
                now,
                completedAt: purchasedAt);

            foreach (var other in open.Where(a => a.Id != purchasedAttempt.Id))
            {
                CloseAttempt(other, FulfilAttemptOutcomes.Superseded, now, completedAt: null);
            }
        }

        ClearLeaseFields(order);
    }

    /// <summary>Marks the latest Purchased attempt as Reverted when purchase stamp is cleared.</summary>
    private async Task RevertPurchaseAttemptAsync(
        Guid orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var latest = await _db.OrderFulfilAttempts
            .Where(a => a.OrderId == orderId && a.Outcome == FulfilAttemptOutcomes.Purchased)
            .OrderByDescending(a => a.CompletedAt ?? a.ClosedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return;
        }

        latest.Outcome = FulfilAttemptOutcomes.Reverted;
        latest.UpdatedAt = now;
    }

    /// <summary>
    /// Closes open attempts of <paramref name="sessionId"/> on orders outside
    /// <paramref name="keepOrderIds"/> so one Chrome profile only focuses one click-group.
    /// When <paramref name="userIdFilter"/> is set, only that user's attempts are closed.
    /// </summary>
    private async Task<List<FulfilClaimItemDto>> ReleaseSessionFocusOutsideKeepAsync(
        Guid sessionId,
        HashSet<Guid> keepOrderIds,
        DateTime now,
        CancellationToken cancellationToken,
        Guid? userIdFilter = null)
    {
        var query = _db.OrderFulfilAttempts
            .Where(a =>
                a.SessionId == sessionId
                && a.ClosedAt == null
                && !keepOrderIds.Contains(a.OrderId));
        if (userIdFilter is { } uid)
        {
            query = query.Where(a => a.UserId == uid);
        }

        var stale = await query.ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return [];
        }

        foreach (var attempt in stale)
        {
            CloseAttempt(attempt, FulfilAttemptOutcomes.Released, now, completedAt: null);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var releasedItems = new List<FulfilClaimItemDto>();
        foreach (var group in stale.GroupBy(a => new { a.OrderId, a.UserId }))
        {
            var order = await _db.Orders.FirstOrDefaultAsync(
                o => o.Id == group.Key.OrderId,
                cancellationToken);
            if (order is null)
            {
                continue;
            }

            if (order.FulfilPurchasedAt is not null)
            {
                continue;
            }

            // Recount for the holder that still owns the lease (or clear if none remain).
            var holderId = order.FulfilClaimedByUserId ?? group.Key.UserId;
            var item = await RecountAndApplyLeaseAsync(order, holderId, now, cancellationToken);
            releasedItems.Add(item);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return releasedItems;
    }

    /// <summary>
    /// Repair stale multi-focus rows: one session may only keep the newest BatchId
    /// (primary + siblings share BatchId). Older batches are Released and leases recounted.
    /// </summary>
    private async Task EnforceSingleBatchFocusPerSessionAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        var open = await _db.OrderFulfilAttempts
            .Where(a => a.ClosedAt == null)
            .Select(a => new
            {
                a.Id,
                a.SessionId,
                a.OrderId,
                a.UserId,
                a.BatchId,
                a.LastRenewedAt,
            })
            .ToListAsync(cancellationToken);

        if (open.Count == 0)
        {
            return;
        }

        var staleIds = new List<Guid>();
        foreach (var sessionGrp in open.GroupBy(a => a.SessionId))
        {
            var batches = sessionGrp
                .GroupBy(a => a.BatchId)
                .Select(g => new
                {
                    BatchId = g.Key,
                    MaxRenewed = g.Max(x => x.LastRenewedAt),
                    Attempts = g.ToList(),
                })
                .OrderByDescending(b => b.MaxRenewed)
                .ToList();

            if (batches.Count <= 1)
            {
                continue;
            }

            foreach (var staleBatch in batches.Skip(1))
            {
                staleIds.AddRange(staleBatch.Attempts.Select(a => a.Id));
            }
        }

        if (staleIds.Count == 0)
        {
            return;
        }

        var staleAttempts = await _db.OrderFulfilAttempts
            .Where(a => staleIds.Contains(a.Id) && a.ClosedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var attempt in staleAttempts)
        {
            CloseAttempt(attempt, FulfilAttemptOutcomes.Released, now, completedAt: null);
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var group in staleAttempts.GroupBy(a => new { a.OrderId, a.UserId }))
        {
            var order = await _db.Orders.FirstOrDefaultAsync(
                o => o.Id == group.Key.OrderId,
                cancellationToken);
            if (order is null || order.FulfilPurchasedAt is not null)
            {
                continue;
            }

            var holderId = order.FulfilClaimedByUserId ?? group.Key.UserId;
            await RecountAndApplyLeaseAsync(order, holderId, now, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<FulfilClaimItemDto> ClaimOneOrderAsync(
        Guid orderId,
        Guid sessionId,
        string? sessionLabel,
        Guid actingUserId,
        DateTime now,
        DateTime newExpiresAt,
        Guid batchId,
        string? customerKey,
        bool checkQuota,
        int maxPerUser,
        CancellationToken cancellationToken)
    {
        // Renew of an existing session on this order does not consume a new quota slot.
        var alreadyMineOpen = await _db.OrderFulfilAttempts
            .AsNoTracking()
            .AnyAsync(
                a => a.OrderId == orderId
                    && a.SessionId == sessionId
                    && a.UserId == actingUserId
                    && a.ClosedAt == null,
                cancellationToken);

        if (checkQuota && !alreadyMineOpen)
        {
            var heldCount = await _db.Orders.CountAsync(
                o => o.FulfilClaimedByUserId == actingUserId
                    && o.FulfilPurchasedAt == null
                    && o.FulfilClaimExpiresAt != null
                    && o.FulfilClaimExpiresAt > now
                    && o.Id != orderId,
                cancellationToken);

            // If this order is already held by me (other session), claiming another session on it
            // does not increase distinct-order count — heldCount already excludes this order only
            // when Id != orderId, so we only block brand-new orders.
            var thisOrderHeldByMe = await _db.Orders.AnyAsync(
                o => o.Id == orderId
                    && o.FulfilClaimedByUserId == actingUserId
                    && o.FulfilClaimExpiresAt != null
                    && o.FulfilClaimExpiresAt > now,
                cancellationToken);

            if (!thisOrderHeldByMe && heldCount >= maxPerUser)
            {
                throw new ConflictException(
                    $"Bạn đang giữ tối đa {maxPerUser} đơn. Nhả hoặc hoàn tất trước khi nhận thêm.");
            }
        }

        var orderForClaim = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        var previousHolderId = orderForClaim.FulfilClaimedByUserId;
        var leaseAlive = orderForClaim.FulfilClaimExpiresAt is { } exp
            && exp > now
            && orderForClaim.FulfilPurchasedAt is null;

        if (orderForClaim.FulfilPurchasedAt is not null)
        {
            throw new ConflictException("Đơn đã được đánh dấu mua — không thể giữ.");
        }

        if (leaseAlive
            && orderForClaim.FulfilClaimedByUserId is { } holder
            && holder != actingUserId)
        {
            var name = await ResolveUserDisplayNameAsync(holder, cancellationToken);
            var remain = orderForClaim.FulfilClaimExpiresAt is { } e
                ? FormatRemain(e - now)
                : "?";
            throw new ConflictException(
                $"Đơn đang do {name ?? "người khác"} giữ (còn {remain}).");
        }

        var keepClaimedAt = leaseAlive
            && orderForClaim.FulfilClaimedByUserId == actingUserId
            && orderForClaim.FulfilClaimedAt is not null;
        var keepExpires = leaseAlive
            && orderForClaim.FulfilClaimedByUserId == actingUserId
            && orderForClaim.FulfilClaimExpiresAt is { } cur
            && cur > newExpiresAt;

        orderForClaim.FulfilClaimedByUserId = actingUserId;
        orderForClaim.FulfilClaimedAt = keepClaimedAt ? orderForClaim.FulfilClaimedAt : now;
        orderForClaim.FulfilClaimExpiresAt = keepExpires
            ? orderForClaim.FulfilClaimExpiresAt
            : newExpiresAt;
        orderForClaim.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        // Previous holder expired → close their leftover open attempts.
        if (previousHolderId is { } prev
            && prev != actingUserId)
        {
            await CloseOpenAttemptsAsync(
                orderId,
                prev,
                sessionIdFilter: null,
                FulfilAttemptOutcomes.Expired,
                now,
                cancellationToken);
        }

        var existing = await _db.OrderFulfilAttempts
            .FirstOrDefaultAsync(
                a => a.OrderId == orderId
                    && a.SessionId == sessionId
                    && a.ClosedAt == null,
                cancellationToken);

        if (existing is not null)
        {
            if (existing.UserId != actingUserId)
            {
                // Should not happen if lease CAS worked; close and recreate.
                CloseAttempt(existing, FulfilAttemptOutcomes.TakenOver, now, completedAt: null);
                existing = null;
            }
            else
            {
                existing.LastRenewedAt = now;
                existing.ExpiresAt = newExpiresAt;
                existing.UpdatedAt = now;
                if (!string.IsNullOrWhiteSpace(sessionLabel))
                {
                    existing.SessionLabel = sessionLabel.Trim();
                }
            }
        }

        if (existing is null)
        {
            _db.OrderFulfilAttempts.Add(new OrderFulfilAttempt
            {
                OrderId = orderId,
                UserId = actingUserId,
                SessionId = sessionId,
                SessionLabel = string.IsNullOrWhiteSpace(sessionLabel)
                    ? null
                    : sessionLabel.Trim(),
                BatchId = batchId,
                CustomerKey = customerKey,
                StartedAt = now,
                LastRenewedAt = now,
                ExpiresAt = newExpiresAt,
                CreatedAt = now,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        var order = await _db.Orders.FirstAsync(o => o.Id == orderId, cancellationToken);
        return await RecountAndApplyLeaseAsync(order, actingUserId, now, cancellationToken);
    }

    private async Task<FulfilClaimItemDto> RecountAndApplyLeaseAsync(
        Order order,
        Guid holderUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Tracked entities (prefer already-loaded CloseAttempt mutations).
        var openRows = await _db.OrderFulfilAttempts
            .Where(a => a.OrderId == order.Id && a.UserId == holderUserId)
            .ToListAsync(cancellationToken);
        var open = openRows
            .Where(a => a.ClosedAt == null)
            .Select(a => new { a.SessionId, a.StartedAt, a.ExpiresAt })
            .ToList();

        if (open.Count == 0)
        {
            ClearLeaseFields(order);
            order.UpdatedAt = now;
            return BuildFreeClaimItem(order.Id, now);
        }

        var maxExp = open.Max(a => a.ExpiresAt);
        var claimedAt = order.FulfilClaimedAt ?? open.Min(a => a.StartedAt);

        order.FulfilClaimedByUserId = holderUserId;
        order.FulfilClaimedAt = claimedAt;
        order.FulfilClaimExpiresAt = maxExp;
        order.FulfilClaimSessionCount = open.Count;
        order.UpdatedAt = now;

        var name = await ResolveUserDisplayNameAsync(holderUserId, cancellationToken);

        return new FulfilClaimItemDto
        {
            OrderId = order.Id,
            State = FulfilClaimStates.Held,
            ClaimedByUserId = holderUserId,
            ClaimedByName = name,
            ClaimedAt = claimedAt,
            ExpiresAt = maxExp,
            SessionCount = open.Count,
            SessionIds = open.Select(a => a.SessionId).ToList(),
            StartedAt = open.Min(a => a.StartedAt),
        };
    }

    private async Task CloseOpenAttemptsAsync(
        Guid orderId,
        Guid userId,
        Guid? sessionIdFilter,
        string outcome,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var q = _db.OrderFulfilAttempts
            .Where(a => a.OrderId == orderId && a.UserId == userId && a.ClosedAt == null);
        if (sessionIdFilter is { } sid)
        {
            q = q.Where(a => a.SessionId == sid);
        }

        var list = await q.ToListAsync(cancellationToken);
        foreach (var a in list)
        {
            CloseAttempt(a, outcome, now, completedAt: null);
        }
    }

    private static void CloseAttempt(
        OrderFulfilAttempt attempt,
        string outcome,
        DateTime now,
        DateTime? completedAt)
    {
        attempt.ClosedAt = now;
        attempt.Outcome = outcome;
        attempt.CompletedAt = completedAt;
        attempt.DurationSeconds = (int)Math.Clamp(
            (now - attempt.StartedAt).TotalSeconds,
            0,
            int.MaxValue);
        attempt.UpdatedAt = now;
    }

    private static void ClearLeaseFields(Order order)
    {
        order.FulfilClaimedByUserId = null;
        order.FulfilClaimedAt = null;
        order.FulfilClaimExpiresAt = null;
        order.FulfilClaimSessionCount = 0;
    }

    /// <summary>Claim còn hiệu lực = chưa hết TTL và chưa stamp mua.</summary>
    private static bool IsClaimActive(Order o, DateTime? now = null)
    {
        var clock = now ?? DateTime.UtcNow;
        return o.FulfilPurchasedAt is null
            && o.FulfilClaimedByUserId is not null
            && o.FulfilClaimExpiresAt is { } exp
            && exp > clock;
    }

    private static FulfilClaimItemDto BuildFreeClaimItem(Guid orderId, DateTime now) =>
        new()
        {
            OrderId = orderId,
            State = FulfilClaimStates.Released,
            SessionCount = 0,
            SessionIds = [],
        };

    private async Task<string?> ResolveUserDisplayNameAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.UserName })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(row.FullName) ? row.UserName : row.FullName;
    }

    private async Task<int> GetClaimTtlMinutesAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(FulfilClaimSettingKeys.TtlMinutes, cancellationToken);
        if (!int.TryParse(raw, out var minutes))
        {
            minutes = int.Parse(FulfilClaimSettingKeys.DefaultTtlMinutes);
        }

        return Math.Clamp(minutes, FulfilClaimSettingKeys.MinTtlMinutes, FulfilClaimSettingKeys.MaxTtlMinutes);
    }

    private async Task<int> GetClaimMaxPerUserAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(FulfilClaimSettingKeys.MaxPerUser, cancellationToken);
        if (!int.TryParse(raw, out var max) || max < 1)
        {
            max = int.Parse(FulfilClaimSettingKeys.DefaultMaxPerUser);
        }

        return max;
    }

    private async Task<ClaimTransaction> BeginDbTransactionAsync(CancellationToken cancellationToken)
    {
        if (_db is not DbContext ef)
        {
            return new ClaimTransaction(null);
        }

        // InMemory provider does not support transactions (tests).
        var provider = ef.Database.ProviderName ?? "";
        if (provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return new ClaimTransaction(null);
        }

        var tx = await ef.Database.BeginTransactionAsync(cancellationToken);
        return new ClaimTransaction(tx);
    }

    private static string FormatRemain(TimeSpan remain)
    {
        if (remain.TotalHours >= 1)
        {
            return $"{(int)remain.TotalHours}h{remain.Minutes:D2}";
        }

        return $"{remain.Minutes:D2}:{remain.Seconds:D2}";
    }

    private sealed class ClaimTransaction : IAsyncDisposable
    {
        private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _tx;

        public ClaimTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx)
        {
            _tx = tx;
        }

        public Task CommitAsync(CancellationToken cancellationToken) =>
            _tx?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            _tx?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
