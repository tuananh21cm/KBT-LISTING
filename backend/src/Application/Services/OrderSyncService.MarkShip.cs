using Application.DTOs.FourSeller;
using Application.DTOs.Order;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Orders;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>Mark Ship reconcile / local publication helpers for <see cref="OrderSyncService"/>.</summary>
public partial class OrderSyncService
{
    /// <summary>Manual UI Mark Ship poll budget (~2 min) — user waits on confirm dialog.</summary>
    private const int ManualMarkShipPollAttempts = 60;

    private const int ManualMarkShipPollDelayMs = 2000;

    /// <summary>
    /// Reconcile / Sync Track finish poll — same budget as interactive auto publish (~60s).
    /// A single get-async call left unconfirmed requests stranded until grace → failed.
    /// </summary>
    private static int ReconcileMarkShipPollAttempts =>
        OrderAutoMarkShipPolicy.InteractiveMarkShipPollAttempts;

    private static int ReconcileMarkShipPollDelayMs =>
        OrderAutoMarkShipPolicy.InteractiveMarkShipPollDelayMs;

    private enum MarkShipReconcileOutcome
    {
        /// <summary>
        /// Could not confirm remote state (fetch failed / order not found) — leave Unconfirmed;
        /// Manual must not blind-POST.
        /// </summary>
        StillPending,

        /// <summary>
        /// Remote order located and confirmed to have no publishable carrier tracking yet
        /// (within grace). Auto leaves Unconfirmed; Manual may clear and POST.
        /// </summary>
        ConfirmedAbsent,

        /// <summary>Remote confirmed same tracking — local order updated to published.</summary>
        Succeeded,

        /// <summary>Remote clearly rejected or grace elapsed with no tracking — safe to retry as Failed.</summary>
        SafeToRetry,

        /// <summary>Remote has a different tracking — stay Blocked.</summary>
        DifferentTracking
    }

    /// <summary>
    /// Applies local order fields after a confirmed Mark Ship (auto, manual, or reconcile).
    /// </summary>
    private void ApplyLocalMarkShipPublication(
        Order order,
        string shippingService,
        string trackingNo,
        OrderFulfilTimeline.Source timelineSource,
        DateTime? trackingAt,
        SupplierPurchase? link = null)
    {
        var now = DateTime.UtcNow;
        order.TrackingNo = trackingNo;
        order.ShippingService = shippingService;
        order.PlatformOrderStatus = PlatformOrderStatuses.AwaitingCollection;
        order.OrderShippedTime ??= now;
        OrderFulfilTimeline.Stamp(
            order, OrderFulfilTimeline.Milestone.TrackingAdded, trackingAt ?? now,
            timelineSource);
        OrderFulfilTimeline.Normalize(order);
        order.LastSyncedAt = now;
        order.UpdatedAt = now;

        if (link is null) return;

        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;
        link.AutoMarkShipError = null;
        link.AutoMarkShipTrackingNo = trackingNo;
        link.AutoMarkShipRequestId = null;
        link.AutoMarkShipAttemptedAt = now;
        link.UpdatedAt = now;
    }

    /// <summary>
    /// When 4Seller sync (or reconcile fetch) already shows published tracking, mark matching
    /// supplier Auto Mark Ship rows as succeeded without calling Mark Ship again.
    /// </summary>
    private static void PromoteAutoMarkShipFromPublishedOrder(Order order)
    {
        var published = order.TrackingNo?.Trim();
        if (string.IsNullOrWhiteSpace(published) || order.SupplierPurchases is null) return;

        foreach (var link in order.SupplierPurchases)
        {
            if (link.AutoMarkShipStatus is AutoMarkShipStatuses.Succeeded) continue;
            if (string.IsNullOrWhiteSpace(link.AutoMarkShipTrackingNo)
                && string.IsNullOrWhiteSpace(link.TrackingNo))
            {
                continue;
            }

            var expected = (link.AutoMarkShipTrackingNo ?? link.TrackingNo)?.Trim();
            if (!string.Equals(expected, published, StringComparison.OrdinalIgnoreCase)) continue;

            link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;
            link.AutoMarkShipError = null;
            link.AutoMarkShipRequestId = null;
            link.AutoMarkShipTrackingNo = published;
            link.AutoMarkShipAttemptedAt ??= DateTime.UtcNow;
            link.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task ProcessAutoMarkShipReconcileAsync(
        IReadOnlyList<AutoMarkShipCandidate> candidates,
        SheinOrderSyncResultDto result,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates
                     .GroupBy(c => c.Link.Id)
                     .Select(g => g.Last()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = candidate.Link;
            if (!OrderAutoMarkShipPolicy.NeedsReconcile(link)
                && !string.Equals(
                    link.AutoMarkShipStatus, AutoMarkShipStatuses.Unconfirmed, StringComparison.Ordinal))
            {
                continue;
            }

            // Mis-attempted Mark Ship on TikTok Shipping — leave the publish loop permanently.
            if (TikTokSellerNetCalculator.IsTikTokPlatformShipping(candidate.Order.ShippingMethod))
            {
                MarkAutoShipBlocked(link, "Đơn TikTok Shipping — không Mark Ship.");
                result.AutoShipBlocked++;
                result.Notes.Add(
                    $"SHEIN {candidate.Billno}: đơn TikTok Shipping — bỏ reconcile Mark Ship.");
                await _db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var outcome = await ReconcileUnconfirmedMarkShipAsync(
                candidate.Order, link, cancellationToken);
            switch (outcome)
            {
                case MarkShipReconcileOutcome.Succeeded:
                    result.AutoShipped++;
                    result.Notes.Add(
                        $"SHEIN {candidate.Billno}: reconcile Mark Ship thành công"
                        + $" · {link.AutoMarkShipTrackingNo ?? link.TrackingNo}.");
                    await _db.SaveChangesAsync(cancellationToken);
                    await TryNotifyFulfilHandlingAsync([candidate.Order], cancellationToken);
                    break;
                case MarkShipReconcileOutcome.SafeToRetry:
                    OrderAutoMarkShipPolicy.DowngradeUnconfirmedToFailed(
                        link,
                        TruncateAutoMarkShipError(
                            "Mark Ship trước đó chưa xác nhận và sàn vẫn chưa có mã — sẽ thử lại sau."));
                    result.AutoShipFailed++;
                    result.Notes.Add(
                        $"SHEIN {candidate.Billno}: unconfirmed → failed (sàn chưa có mã; retry sau cooldown).");
                    await _db.SaveChangesAsync(cancellationToken);
                    break;
                case MarkShipReconcileOutcome.ConfirmedAbsent:
                    // Within grace — Auto waits; Manual Đẩy track may republish separately.
                    break;
                case MarkShipReconcileOutcome.DifferentTracking:
                    result.AutoShipBlocked++;
                    result.Notes.Add(
                        $"SHEIN {candidate.Billno}: {link.AutoMarkShipError}");
                    await _db.SaveChangesAsync(cancellationToken);
                    break;
            }
        }
    }

    private async Task<MarkShipReconcileOutcome> ReconcileUnconfirmedMarkShipAsync(
        Order order,
        SupplierPurchase link,
        CancellationToken cancellationToken)
    {
        var expectedTracking = (link.AutoMarkShipTrackingNo ?? link.TrackingNo)?.Trim() ?? string.Empty;
        var now = DateTime.UtcNow;

        // Fast path: local order already has matching published tracking (e.g. 4Seller sync).
        if (!string.IsNullOrWhiteSpace(order.TrackingNo)
            && string.Equals(order.TrackingNo.Trim(), expectedTracking, StringComparison.OrdinalIgnoreCase))
        {
            PromoteAutoMarkShipFromPublishedOrder(order);
            return MarkShipReconcileOutcome.Succeeded;
        }

        // Retry async poll if we still have the request id — long loop like manual finish,
        // not a single get-async peek (that left Sync Track unable to confirm).
        if (!string.IsNullOrWhiteSpace(link.AutoMarkShipRequestId))
        {
            try
            {
                var poll = await PollMarkShipResultAsync(
                    await OrderFourSellerOwner.ResolveAsync(order, _db.Shops, cancellationToken),
                    link.AutoMarkShipRequestId,
                    cancellationToken,
                    ReconcileMarkShipPollAttempts,
                    ReconcileMarkShipPollDelayMs);
                if (IsMarkShipSuccess(poll))
                {
                    var service = order.ShippingService
                        ?? link.Carrier
                        ?? string.Empty;
                    ApplyLocalMarkShipPublication(
                        order,
                        service.Trim().Length > 0 ? service.Trim() : (link.Carrier ?? "Unknown"),
                        expectedTracking,
                        OrderFulfilTimeline.Source.Shein,
                        link.AutoMarkShipAttemptedAt,
                        link);
                    return MarkShipReconcileOutcome.Succeeded;
                }

                if (OrderAutoMarkShipPolicy.IsExplicitMarkShipReject(poll.Status, poll.Message))
                {
                    link.AutoMarkShipStatus = AutoMarkShipStatuses.Failed;
                    link.AutoMarkShipError = TruncateAutoMarkShipError(poll.Message);
                    link.AutoMarkShipRequestId = null;
                    link.AutoMarkShipAttemptedAt = now;
                    return MarkShipReconcileOutcome.SafeToRetry;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "Mark Ship reconcile poll failed for order {OrderId} request {RequestId}",
                    order.Id,
                    link.AutoMarkShipRequestId);
            }
        }

        FourSellerOrderRecordDto? remote = null;
        try
        {
            remote = await FetchRemoteOrderForMarkShipReconcileAsync(order, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Mark Ship reconcile fetch failed for order {OrderId}",
                order.Id);
        }

        if (remote is not null)
        {
            var remoteTracking = remote.TrackingNo?.Trim() ?? string.Empty;
            if (remoteTracking.Length > 0)
            {
                var remoteService = remote.ShippingServiceInfo?.ShippingService;
                if (OrderFourSellerStatusApply.ClassifyTracking(remoteTracking, remoteService)
                    == SupplierTrackingKinds.Warehouse)
                {
                    // Warehouse codes on the remote payload are not proof our carrier Mark Ship landed.
                    // Fall through: remote was found but carrier tracking still absent.
                }
                else if (string.Equals(remoteTracking, expectedTracking, StringComparison.OrdinalIgnoreCase)
                         || expectedTracking.Length == 0)
                {
                    var service = remoteService?.Trim()
                        ?? order.ShippingService
                        ?? link.Carrier
                        ?? string.Empty;
                    OrderFourSellerStatusApply.ApplyTrackingFromRecord(order, remote);
                    if (string.IsNullOrWhiteSpace(order.TrackingNo))
                    {
                        ApplyLocalMarkShipPublication(
                            order,
                            service.Length > 0 ? service : "Unknown",
                            remoteTracking,
                            OrderFulfilTimeline.Source.Platform,
                            null,
                            link);
                    }
                    else
                    {
                        PromoteAutoMarkShipFromPublishedOrder(order);
                        link.AutoMarkShipStatus = AutoMarkShipStatuses.Succeeded;
                        link.AutoMarkShipError = null;
                        link.AutoMarkShipRequestId = null;
                    }

                    return MarkShipReconcileOutcome.Succeeded;
                }
                else
                {
                    MarkAutoShipBlocked(
                        link,
                        $"Sàn đã có tracking khác ({remoteTracking}) — không đẩy lại {expectedTracking}.");
                    link.AutoMarkShipRequestId = null;
                    return MarkShipReconcileOutcome.DifferentTracking;
                }
            }

            // Remote order found and confirmed to have no carrier tracking yet.
            var attemptedAt = link.AutoMarkShipAttemptedAt ?? now;
            if (now - attemptedAt >= OrderAutoMarkShipPolicy.UnconfirmedGracePeriod)
            {
                return MarkShipReconcileOutcome.SafeToRetry;
            }

            return MarkShipReconcileOutcome.ConfirmedAbsent;
        }

        // Fetch failed or order not found in searched buckets — do NOT downgrade to Failed.
        // Blind retry here risks double-publish if TikTok already accepted the first request.
        return MarkShipReconcileOutcome.StillPending;
    }

    private async Task<FourSellerOrderRecordDto?> FetchRemoteOrderForMarkShipReconcileAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var orderNo = order.PlatformOrderId?.Trim() ?? string.Empty;
        if (orderNo.Length == 0) return null;

        // Bucket "all" + exact orderNo: một truy vấn phủ mọi trạng thái. Danh sách cũ
        // (shipped/to_ship/in_process) bỏ sót đơn đã rơi về on_hold hoặc canceled sau khi publish,
        // nên reconcile trả null và đơn kẹt ở unconfirmed.
        var page = await _fourSeller.GetOrderPageAsync(
            await OrderFourSellerOwner.ResolveAsync(order, _db.Shops, cancellationToken),
            new FourSellerOrderPageRequest
            {
                OrderStatus = AllStatusBucket,
                PageCurrent = 1,
                PageSize = 10,
                SearchType = "orderNo",
                SearchMethod = "exact",
                SearchValue = orderNo,
                Platform = order.Platform?.Trim() ?? string.Empty
            },
            cancellationToken);

        return MatchRemoteOrderRecord(page.Records, order.FourSellerOrderId, orderNo);
    }

    /// <summary>
    /// Before manual Mark Ship: reconcile any unconfirmed auto attempt so we never double-publish.
    /// Returns a success DTO when the same tracking is already on the platform.
    /// </summary>
    private async Task<MarkShipResultDto?> TryReconcileBeforeManualMarkShipAsync(
        Order order,
        string trackingNo,
        CancellationToken cancellationToken)
    {
        // Local already published — never call to-ship-mark-ship again.
        if (!string.IsNullOrWhiteSpace(order.TrackingNo))
        {
            if (string.Equals(order.TrackingNo.Trim(), trackingNo, StringComparison.OrdinalIgnoreCase))
            {
                PromoteAutoMarkShipFromPublishedOrder(order);
                await _db.SaveChangesAsync(cancellationToken);
                return new MarkShipResultDto
                {
                    Success = true,
                    Message = "Tracking đã có trên đơn local — không Mark Ship lại.",
                    TrackingNo = order.TrackingNo,
                    PlatformOrderStatus = order.PlatformOrderStatus
                };
            }

            throw new BusinessException(
                $"Đơn đã có tracking '{order.TrackingNo}' — không thể Mark Ship mã '{trackingNo}'."
                + " Kiểm tra trên sàn trước khi xử lý tay.");
        }

        var links = order.SupplierPurchases?
            .Where(l => OrderAutoMarkShipPolicy.NeedsReconcile(l)
                || string.Equals(
                    l.AutoMarkShipStatus, AutoMarkShipStatuses.Unconfirmed, StringComparison.Ordinal))
            .ToList()
            ?? [];

        foreach (var link in links)
        {
            var outcome = await ReconcileUnconfirmedMarkShipAsync(order, link, cancellationToken);
            if (outcome == MarkShipReconcileOutcome.Succeeded)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (outcome == MarkShipReconcileOutcome.SafeToRetry)
            {
                OrderAutoMarkShipPolicy.DowngradeUnconfirmedToFailed(
                    link,
                    TruncateAutoMarkShipError(
                        "Mark Ship trước đó chưa xác nhận và sàn vẫn chưa có mã — sẽ thử lại sau."));
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (outcome == MarkShipReconcileOutcome.DifferentTracking)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        if (!string.IsNullOrWhiteSpace(order.TrackingNo)
            && string.Equals(order.TrackingNo.Trim(), trackingNo, StringComparison.OrdinalIgnoreCase))
        {
            return new MarkShipResultDto
            {
                Success = true,
                Message = "Tracking đã có trên sàn — đã đồng bộ local, không Mark Ship lại.",
                TrackingNo = order.TrackingNo,
                PlatformOrderStatus = order.PlatformOrderStatus
            };
        }

        if (!string.IsNullOrWhiteSpace(order.TrackingNo))
        {
            throw new BusinessException(
                $"Sàn/local đã có tracking '{order.TrackingNo}' — không đẩy mã '{trackingNo}'.");
        }

        // No unconfirmed links: still peek remote once so a prior manual unconfirmed
        // (or lost requestId) cannot double-publish.
        try
        {
            var remote = await FetchRemoteOrderForMarkShipReconcileAsync(order, cancellationToken);
            if (remote is not null)
            {
                var remoteTracking = remote.TrackingNo?.Trim() ?? string.Empty;
                if (remoteTracking.Length > 0
                    && OrderFourSellerStatusApply.ClassifyTracking(
                        remoteTracking, remote.ShippingServiceInfo?.ShippingService)
                    != SupplierTrackingKinds.Warehouse)
                {
                    if (string.Equals(remoteTracking, trackingNo, StringComparison.OrdinalIgnoreCase))
                    {
                        var service = remote.ShippingServiceInfo?.ShippingService?.Trim()
                            ?? order.ShippingService
                            ?? "Unknown";
                        ApplyLocalMarkShipPublication(
                            order,
                            service,
                            remoteTracking,
                            OrderFulfilTimeline.Source.Platform,
                            null,
                            order.SupplierPurchases?.FirstOrDefault(l =>
                                l.TrackingKind == SupplierTrackingKinds.Carrier
                                && string.Equals(
                                    (l.TrackingNo ?? l.AutoMarkShipTrackingNo)?.Trim(),
                                    trackingNo,
                                    StringComparison.OrdinalIgnoreCase)));
                        await _db.SaveChangesAsync(cancellationToken);
                        await TryNotifyFulfilHandlingAsync([order], cancellationToken);
                        return new MarkShipResultDto
                        {
                            Success = true,
                            Message = "Tracking đã có trên sàn — đã đồng bộ local, không Mark Ship lại.",
                            TrackingNo = order.TrackingNo,
                            PlatformOrderStatus = order.PlatformOrderStatus
                        };
                    }

                    throw new BusinessException(
                        $"Sàn đã có tracking '{remoteTracking}' — không đẩy mã '{trackingNo}'.");
                }
            }
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Manual Mark Ship pre-check fetch failed for order {OrderId}",
                order.Id);
        }

        return null;
    }

    /// <summary>
    /// Manual retry when a prior Mark Ship left the carrier link <c>unconfirmed</c>: poll the
    /// existing requestId first; if still not confirmed, reconcile against the remote order.
    /// </summary>
    private async Task<(MarkShipResultDto? Success, MarkShipReconcileOutcome Outcome)>
        TryFinishPendingManualMarkShipAsync(
            Order order,
            SupplierPurchase link,
            string trackingNo,
            string shippingService,
            CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(link.AutoMarkShipRequestId))
        {
            var poll = await PollMarkShipResultAsync(
                await OrderFourSellerOwner.ResolveAsync(order, _db.Shops, cancellationToken),
                link.AutoMarkShipRequestId,
                cancellationToken,
                ManualMarkShipPollAttempts,
                ManualMarkShipPollDelayMs);
            if (IsMarkShipSuccess(poll))
            {
                var dto = await CompleteManualMarkShipPublicationAsync(
                    order,
                    shippingService,
                    trackingNo,
                    link,
                    poll.Message,
                    link.AutoMarkShipRequestId,
                    cancellationToken);
                return (dto, MarkShipReconcileOutcome.Succeeded);
            }
        }

        var outcome = await ReconcileUnconfirmedMarkShipAsync(order, link, cancellationToken);
        if (outcome == MarkShipReconcileOutcome.Succeeded)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await TryNotifyFulfilHandlingAsync([order], cancellationToken);
            return (BuildMarkShipSuccessDto(order, trackingNo, link.AutoMarkShipRequestId), outcome);
        }

        if (outcome == MarkShipReconcileOutcome.DifferentTracking)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return (null, outcome);
    }

    /// <summary>
    /// After async poll times out, tracking may already be on the platform — read once before
    /// leaving the attempt <c>unconfirmed</c>.
    /// </summary>
    private async Task<MarkShipResultDto?> TryRescueMarkShipFromRemoteAsync(
        Order order,
        string trackingNo,
        string shippingService,
        OrderFulfilTimeline.Source timelineSource,
        DateTime? trackingAt,
        SupplierPurchase? link,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var remote = await FetchRemoteOrderForMarkShipReconcileAsync(order, cancellationToken);
            if (remote is null)
            {
                return null;
            }

            var remoteTracking = remote.TrackingNo?.Trim() ?? string.Empty;
            if (remoteTracking.Length == 0
                || OrderFourSellerStatusApply.ClassifyTracking(
                    remoteTracking, remote.ShippingServiceInfo?.ShippingService)
                == SupplierTrackingKinds.Warehouse)
            {
                return null;
            }

            if (!string.Equals(remoteTracking, trackingNo, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var service = remote.ShippingServiceInfo?.ShippingService?.Trim()
                ?? shippingService.Trim();
            if (service.Length == 0)
            {
                service = shippingService.Trim();
            }

            ApplyLocalMarkShipPublication(
                order,
                service.Length > 0 ? service : "Unknown",
                remoteTracking,
                timelineSource,
                trackingAt,
                link);
            await _db.SaveChangesAsync(cancellationToken);
            await TryNotifyFulfilHandlingAsync([order], cancellationToken);

            return BuildMarkShipSuccessDto(
                order,
                remoteTracking,
                requestId,
                "Tracking đã có trên sàn — đã đồng bộ local sau khi poll async chậm.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Mark Ship remote rescue failed for order {OrderId}",
                order.Id);
            return null;
        }
    }

    private async Task<MarkShipResultDto> CompleteManualMarkShipPublicationAsync(
        Order order,
        string shippingService,
        string trackingNo,
        SupplierPurchase? link,
        string? pollMessage,
        string? requestId,
        CancellationToken cancellationToken)
    {
        ApplyLocalMarkShipPublication(
            order,
            shippingService,
            trackingNo,
            OrderFulfilTimeline.Source.Manual,
            DateTime.UtcNow,
            link);
        await _db.SaveChangesAsync(cancellationToken);
        await TryNotifyFulfilHandlingAsync([order], cancellationToken);

        return BuildMarkShipSuccessDto(
            order,
            trackingNo,
            requestId,
            pollMessage ?? "Mark Shipped thành công.");
    }

    private static MarkShipResultDto BuildMarkShipSuccessDto(
        Order order,
        string trackingNo,
        string? requestId,
        string? message = null) =>
        new()
        {
            Success = true,
            Message = message ?? "Mark Shipped thành công.",
            TrackingNo = trackingNo,
            RequestId = requestId,
            PlatformOrderStatus = order.PlatformOrderStatus
        };

    private static bool IsPendingManualMarkShipRetry(SupplierPurchase link) =>
        OrderAutoMarkShipPolicy.NeedsReconcile(link)
        || string.Equals(
            link.AutoMarkShipStatus, AutoMarkShipStatuses.Unconfirmed, StringComparison.Ordinal);
}
