using System.Diagnostics;
using Application.DTOs.Order;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <inheritdoc />
public class OrderWorkflowSignalsVerifier : IOrderWorkflowSignalsVerifier
{
    private const int BatchSize = 500;
    private const int MaxSamples = 20;

    private readonly IApplicationDbContext _db;
    private readonly ILogger<OrderWorkflowSignalsVerifier> _logger;

    /// <summary>Khởi tạo verifier.</summary>
    public OrderWorkflowSignalsVerifier(
        IApplicationDbContext db,
        ILogger<OrderWorkflowSignalsVerifier> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrderWorkflowSignalsDriftDto> VerifyAsync(
        int maxOrders,
        bool repair,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new OrderWorkflowSignalsDriftDto();
        var driftedIds = new List<Guid>();

        var remaining = maxOrders > 0 ? maxOrders : int.MaxValue;
        var offset = 0;

        while (remaining > 0 && !cancellationToken.IsCancellationRequested)
        {
            var take = Math.Min(BatchSize, remaining);

            // Slim read: only the columns the rules touch, never RawPayload or buyer data.
            var rows = await _db.Orders
                .AsNoTracking()
                .OrderByDescending(o => o.OrderPaidTime ?? o.OrderCreateTime ?? o.CreatedAt)
                .ThenBy(o => o.Id)
                .Skip(offset)
                .Take(take)
                .Select(o => new SignalRow
                {
                    Id = o.Id,
                    PlatformOrderId = o.PlatformOrderId,
                    PlatformOrderStatus = o.PlatformOrderStatus,
                    FulfilPurchasedAt = o.FulfilPurchasedAt,
                    FulfilAccountId = o.FulfilAccountId,
                    TrackingNo = o.TrackingNo,
                    ShippingService = o.ShippingService,
                    StoredTab = o.WorkflowTab,
                    StoredCarrier = o.HasCarrierTracking,
                    StoredPurchase = o.HasSupplierPurchase,
                    StoredUsps = o.HasUspsSignal,
                    StoredWarehouse = o.HasWarehouseSignal,
                    Links = o.SupplierPurchases
                        .Select(l => new SupplierPurchase
                        {
                            TrackingKind = l.TrackingKind,
                            TrackingNo = l.TrackingNo,
                            Carrier = l.Carrier,
                            WarehouseTrackingNo = l.WarehouseTrackingNo
                        })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                report.Scanned++;
                if (row.StoredTab is null)
                {
                    report.UnstampedCount++;
                }

                var probe = row.ToProbe();
                OrderWorkflowSignals.Apply(probe);

                var flagDrift = new List<string>();
                if (probe.HasCarrierTracking != row.StoredCarrier)
                {
                    flagDrift.Add($"HasCarrierTracking: {row.StoredCarrier}→{probe.HasCarrierTracking}");
                }

                if (probe.HasSupplierPurchase != row.StoredPurchase)
                {
                    flagDrift.Add($"HasSupplierPurchase: {row.StoredPurchase}→{probe.HasSupplierPurchase}");
                }

                if (probe.HasUspsSignal != row.StoredUsps)
                {
                    flagDrift.Add($"HasUspsSignal: {row.StoredUsps}→{probe.HasUspsSignal}");
                }

                if (probe.HasWarehouseSignal != row.StoredWarehouse)
                {
                    flagDrift.Add($"HasWarehouseSignal: {row.StoredWarehouse}→{probe.HasWarehouseSignal}");
                }

                var tabDrift = !string.Equals(probe.WorkflowTab, row.StoredTab, StringComparison.Ordinal);
                if (!tabDrift && flagDrift.Count == 0)
                {
                    continue;
                }

                report.DriftCount++;
                driftedIds.Add(row.Id);
                if (report.Samples.Count < MaxSamples)
                {
                    report.Samples.Add(new OrderWorkflowSignalsDriftRowDto
                    {
                        OrderId = row.Id,
                        PlatformOrderId = row.PlatformOrderId,
                        StoredWorkflowTab = row.StoredTab,
                        ExpectedWorkflowTab = probe.WorkflowTab,
                        FlagDrift = flagDrift
                    });
                }
            }

            remaining -= rows.Count;
            offset += rows.Count;
            if (rows.Count < take)
            {
                break;
            }
        }

        if (repair && driftedIds.Count > 0)
        {
            report.RepairedCount = await RepairAsync(driftedIds, cancellationToken);
        }

        report.DurationMs = sw.ElapsedMilliseconds;

        if (report.DriftCount > 0)
        {
            _logger.LogWarning(
                "OrderWorkflowSignals drift: {DriftCount}/{Scanned} đơn lệch cột denormalize "
                + "(chưa stamp {Unstamped}, đã sửa {Repaired}). Ví dụ: {Samples}",
                report.DriftCount,
                report.Scanned,
                report.UnstampedCount,
                report.RepairedCount,
                string.Join("; ", report.Samples.Select(s =>
                    $"{s.PlatformOrderId ?? s.OrderId.ToString()} {s.StoredWorkflowTab ?? "(null)"}→{s.ExpectedWorkflowTab}")));
        }
        else
        {
            _logger.LogInformation(
                "OrderWorkflowSignals verify: {Scanned} đơn khớp cột denormalize ({DurationMs} ms).",
                report.Scanned,
                report.DurationMs);
        }

        return report;
    }

    /// <summary>
    /// Rewrites the drifted rows. The <c>SaveChangesAsync</c> writer recomputes them anyway, so
    /// loading them with their links is enough — no column is set here.
    /// </summary>
    private async Task<int> RepairAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var repaired = 0;
        foreach (var chunk in ids.Chunk(BatchSize))
        {
            var orders = await _db.Orders
                .Where(o => chunk.Contains(o.Id))
                .Include(o => o.SupplierPurchases)
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                if (OrderWorkflowSignals.Apply(order))
                {
                    repaired++;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        return repaired;
    }

    /// <summary>Slim read shape: exactly the columns the derivation rules read.</summary>
    private sealed class SignalRow
    {
        public Guid Id { get; init; }
        public string? PlatformOrderId { get; init; }
        public string? PlatformOrderStatus { get; init; }
        public DateTime? FulfilPurchasedAt { get; init; }
        public Guid? FulfilAccountId { get; init; }
        public string? TrackingNo { get; init; }
        public string? ShippingService { get; init; }
        public string? StoredTab { get; init; }
        public bool StoredCarrier { get; init; }
        public bool StoredPurchase { get; init; }
        public bool StoredUsps { get; init; }
        public bool StoredWarehouse { get; init; }
        public List<SupplierPurchase> Links { get; init; } = [];

        /// <summary>An unattached order carrying only the source fields, for recomputing.</summary>
        public Order ToProbe() => new()
        {
            Id = Id,
            PlatformOrderStatus = PlatformOrderStatus,
            FulfilPurchasedAt = FulfilPurchasedAt,
            FulfilAccountId = FulfilAccountId,
            TrackingNo = TrackingNo,
            ShippingService = ShippingService,
            SupplierPurchases = Links
        };
    }
}
