using Shared.Constants;

namespace Application.Interfaces;

/// <summary>
/// Per-seller listing sync coordination: exclusive sync (manual/background/extension),
/// and concurrent auto publish (multiple shops per seller). Extension presence leases
/// block background sync and auto publish while the extension is syncing.
/// </summary>
public interface IListingSellerSyncLease
{
    /// <summary>
    /// Marks the seller as extension-active for <paramref name="ttl"/> so background
    /// sync skips them. Does not block Manual sync.
    /// </summary>
    void TouchExtensionLease(Guid ownerId, TimeSpan ttl);

    /// <summary>Whether an unexpired extension presence lease exists.</summary>
    bool HasActiveExtensionLease(Guid ownerId);

    /// <summary>
    /// Begins a seller-scoped operation. Manual, background sync, and extension are
    /// exclusive (one at a time). Auto publish allows multiple concurrent operations
    /// per seller. Returns false when blocked. Dispose the handle to release.
    /// </summary>
    bool TryBegin(Guid ownerId, ListingSyncLeaseSource source, out IDisposable? handle);
}
