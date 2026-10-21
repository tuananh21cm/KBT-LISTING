using Application.Services;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seeding;

/// <summary>
/// Stamps <see cref="Order.WorkflowTab"/> and the <c>Has…</c> signal flags on rows that predate the
/// columns. Runs in batches keyed on "tab is still null" so an interrupted startup simply resumes.
///
/// Unlike the other backfills this one has no completion marker: the "not yet stamped" predicate is
/// the marker, and it also picks up rows a failed write left behind.
/// </summary>
public static class OrderWorkflowSignalsBackfill
{
    private const int BatchSize = 500;

    /// <summary>Stamps every order whose <see cref="Order.WorkflowTab"/> is still null.</summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pending = await context.Orders
            .CountAsync(o => o.WorkflowTab == null, cancellationToken);
        if (pending == 0)
        {
            return;
        }

        logger.LogInformation(
            "OrderWorkflowSignalsBackfill: stamping workflow signals cho {Count} đơn.",
            pending);

        var stamped = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            // Ordered by Id so the paging is stable while the previous batch is being written.
            var batch = await context.Orders
                .Where(o => o.WorkflowTab == null)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .Include(o => o.SupplierPurchases)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var order in batch)
            {
                OrderWorkflowSignals.Apply(order);
            }

            // Resolve never returns null, so every row in this batch leaves the pending set even
            // when Apply reports no change — no risk of looping on the same batch.
            await context.SaveChangesAsync(cancellationToken);
            stamped += batch.Count;

            foreach (var entry in context.ChangeTracker.Entries<Order>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            foreach (var entry in context.ChangeTracker.Entries<SupplierPurchase>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        logger.LogInformation(
            "OrderWorkflowSignalsBackfill: đã stamp {Count} đơn.",
            stamped);
    }
}
