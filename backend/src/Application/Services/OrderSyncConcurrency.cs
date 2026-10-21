using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// How to recover a <see cref="DbUpdateConcurrencyException"/> entry during order sync.
/// </summary>
public enum OrderSyncConcurrencyAction
{
    /// <summary>Row is gone and we no longer need it (delete already applied, or do not resurrect).</summary>
    Detach,

    /// <summary>Row is gone but 4Seller still wants this line item — INSERT with the same key.</summary>
    Reinsert,

    /// <summary>Row still exists; refresh original values so the current client values can save.</summary>
    RefreshOriginals
}

/// <summary>
/// Resolves optimistic-concurrency misses on order upsert. EF reports 0 rows affected when a
/// tracked UPDATE/DELETE races a concurrent delete (duplicate merge, empty item-list wipe, SHEIN).
/// Blind detach of <see cref="EntityState.Modified"/> OrderItems drops the 4Seller payload.
/// </summary>
public static class OrderSyncConcurrency
{
    /// <summary>
    /// Missing <see cref="Domain.Entities.OrderItem"/> that we still intend to write is re-inserted;
    /// missing <see cref="Domain.Entities.Order"/> / other types are detached so a duplicate-merge
    /// delete is not undone. Existing rows keep current values and refresh originals.
    /// </summary>
    public static OrderSyncConcurrencyAction Resolve(
        Type entityType,
        EntityState state,
        bool databaseRowExists)
    {
        if (databaseRowExists)
        {
            return OrderSyncConcurrencyAction.RefreshOriginals;
        }

        if (state is EntityState.Modified or EntityState.Added
            && entityType == typeof(Domain.Entities.OrderItem))
        {
            return OrderSyncConcurrencyAction.Reinsert;
        }

        return OrderSyncConcurrencyAction.Detach;
    }
}
