namespace Shared.Constants;

/// <summary>Bulk listing pull modes for <c>ListingSyncRequest.SyncMode</c> (legacy wire values).</summary>
public static class ListingSyncModes
{
    /// <summary>Legacy alias for <see cref="ListingSyncScopes.Status"/>.</summary>
    public const string Light = "light";

    /// <summary>Legacy alias for <see cref="ListingSyncScopes.Full"/>.</summary>
    public const string Full = "full";

    /// <summary>Published status-only pull (preferred wire value).</summary>
    public const string Status = "status";

    /// <summary>True when mode is missing, <see cref="Light"/>, or <see cref="Status"/>.</summary>
    public static bool IsLight(string? mode) =>
        string.IsNullOrWhiteSpace(mode)
        || string.Equals(mode.Trim(), Light, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode.Trim(), Status, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Resolved bulk sync scope: draft content (<see cref="Full"/>) vs published status-only (<see cref="Status"/>).
/// </summary>
public static class ListingSyncScopes
{
    /// <summary>Draft pull: page + batch-detail, full <c>ApplyRecord</c> + variations.</summary>
    public const string Full = "full";

    /// <summary>Published pull: page only, status fields via <c>ApplyStatusRecord</c>.</summary>
    public const string Status = "status";

    /// <summary>
    /// Resolves scope from <paramref name="syncScope"/> or legacy <paramref name="syncMode"/>.
    /// </summary>
    public static string Resolve(string? syncScope, string? syncMode)
    {
        if (!string.IsNullOrWhiteSpace(syncScope))
        {
            var scoped = syncScope.Trim();
            if (string.Equals(scoped, Full, StringComparison.OrdinalIgnoreCase))
            {
                return Full;
            }

            if (string.Equals(scoped, Status, StringComparison.OrdinalIgnoreCase))
            {
                return Status;
            }
        }

        if (string.Equals(syncMode?.Trim(), Full, StringComparison.OrdinalIgnoreCase))
        {
            return Full;
        }

        // light, status, empty → status-only (published default).
        return Status;
    }

    public static bool IsStatus(string scope) =>
        string.Equals(scope, Status, StringComparison.OrdinalIgnoreCase);

    public static bool IsFull(string scope) =>
        string.Equals(scope, Full, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Who initiated a seller-scoped listing sync lease.</summary>
public enum ListingSyncLeaseSource
{
    /// <summary>Admin/seller UI Sync button or API.</summary>
    Manual = 0,

    /// <summary><c>ListingDraftSyncBackgroundService</c> / <c>ListingPublishedSyncBackgroundService</c>.</summary>
    Background = 1,

    /// <summary>Post-html / push scheduler (extension-driven).</summary>
    Extension = 2,

    /// <summary>Auto publish pipeline — multiple concurrent per seller (bounded by global gate).</summary>
    AutoPublish = 3
}

/// <summary>Lifecycle of a manual listing sync job (<c>ListingSyncJobStatusDto.State</c>).</summary>
public static class ListingSyncJobStates
{
    /// <summary>Still pulling; the client should keep polling.</summary>
    public const string Running = "running";

    /// <summary>Finished — individual sellers may still have failed.</summary>
    public const string Completed = "completed";

    /// <summary>The run itself threw before it could report per-seller results.</summary>
    public const string Failed = "failed";

    /// <summary>True once polling can stop.</summary>
    public static bool IsTerminal(string? state) =>
        string.Equals(state, Completed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, Failed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Per-seller progress inside a listing sync job.</summary>
public static class ListingSyncSellerStatuses
{
    /// <summary>Queued behind the seller concurrency limit.</summary>
    public const string Pending = "pending";

    /// <summary>Currently pulling from 4Seller.</summary>
    public const string Running = "running";

    /// <summary>Finished and counted.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Failed or ran out of its time budget; other sellers are unaffected.</summary>
    public const string Failed = "failed";

    /// <summary>Another pull already held this seller's lease.</summary>
    public const string SkippedBusy = "skipped_busy";
}

/// <summary>Wire values for <c>ListingSyncRequest.SyncSource</c> (server-set).</summary>
public static class ListingSyncSources
{
    /// <summary>Admin/seller list Sync button.</summary>
    public const string Manual = "manual";

    /// <summary><c>ListingDraftSyncBackgroundService</c> / <c>ListingPublishedSyncBackgroundService</c>.</summary>
    public const string Background = "background";

    /// <summary>Post-html / push scheduler (extension-driven).</summary>
    public const string Extension = "extension";

    /// <summary>Parses wire value; unknown → Manual.</summary>
    public static ListingSyncLeaseSource Parse(string? raw)
    {
        if (string.Equals(raw, Background, StringComparison.OrdinalIgnoreCase))
        {
            return ListingSyncLeaseSource.Background;
        }

        if (string.Equals(raw, Extension, StringComparison.OrdinalIgnoreCase))
        {
            return ListingSyncLeaseSource.Extension;
        }

        return ListingSyncLeaseSource.Manual;
    }
}
