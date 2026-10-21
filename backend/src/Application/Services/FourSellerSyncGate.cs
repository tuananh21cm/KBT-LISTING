namespace Application.Services;

/// <summary>
/// Process-wide gate serialising every flow that upserts <c>Orders</c> from 4Seller.
///
/// Order sync and after-sales sync run on separate DI scopes, so each holds its own
/// <c>DbContext</c> with its own change tracker. Letting them overlap means both load the same
/// row, mutate different columns, and the second <c>SaveChanges</c> writes back the stale values
/// it read for the columns the first one changed — the refund state would silently flip back.
/// </summary>
public static class FourSellerSyncGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Tries to enter without waiting. Callers that fail must skip the run and retry later:
    /// queueing background passes behind a 15-minute order sync only piles up stale work.
    /// </summary>
    public static Task<bool> TryEnterAsync(CancellationToken cancellationToken = default) =>
        Gate.WaitAsync(0, cancellationToken);

    /// <summary>Releases the gate. Always call from a <c>finally</c> after a successful enter.</summary>
    public static void Exit() => Gate.Release();
}
