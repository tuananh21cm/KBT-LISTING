using System.Collections.Concurrent;
using Application.DTOs.Listing;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Singleton debounce queue: post-html / Save Draft / Publish / … → one async sync per seller.
/// Never blocks the HTTP response that triggered <see cref="Schedule"/>.
/// </summary>
public sealed class PostHtmlDraftSyncScheduler : IPostHtmlDraftSyncScheduler, IDisposable
{
    /// <summary>Trailing quiet window for post-html multi-tab coalesce.</summary>
    internal static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(12);

    /// <summary>Wait between per-seller lease-busy retries (manual sync on same account).</summary>
    internal static readonly TimeSpan BusyRetryDelay = TimeSpan.FromSeconds(8);

    /// <summary>~3 minutes of busy retries after the debounce window.</summary>
    internal const int BusyRetryMaxAttempts = 24;

    /// <summary>How long background sync should skip this seller after schedule / during pull.</summary>
    internal static readonly TimeSpan ExtensionLeaseTtl = TimeSpan.FromSeconds(90);

    private const int DraftSyncPageSize = 20;

    private readonly ConcurrentDictionary<Guid, PendingEntry> _pending = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IListingSellerSyncLease _sellerLease;
    private readonly ILogger<PostHtmlDraftSyncScheduler> _logger;
    private bool _disposed;

    /// <summary>Creates the scheduler.</summary>
    public PostHtmlDraftSyncScheduler(
        IServiceScopeFactory scopeFactory,
        IListingSellerSyncLease sellerLease,
        ILogger<PostHtmlDraftSyncScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _sellerLease = sellerLease;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Schedule(
        Guid ownerId,
        IReadOnlyList<string>? statuses = null,
        TimeSpan? debounce = null,
        string? syncScope = null,
        IReadOnlyList<string>? listingIds = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ownerId == Guid.Empty) return;

        // Presence lease so background job skips this seller during debounce + pull.
        _sellerLease.TouchExtensionLease(ownerId, ExtensionLeaseTtl);

        var resolved = ResolveStatuses(statuses);
        var resolvedScope = ResolveSyncScope(syncScope, resolved);
        var wait = debounce ?? DefaultDebounce;
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;

        var entry = _pending.GetOrAdd(ownerId, static _ => new PendingEntry());
        CancellationToken token;
        int version;
        lock (entry.Gate)
        {
            entry.Cts.Cancel();
            entry.Cts.Dispose();
            entry.Cts = new CancellationTokenSource();
            entry.Version++;
            entry.Statuses = resolved;
            entry.SyncScope = resolvedScope;
            entry.Debounce = wait;
            if (listingIds is { Count: > 0 })
            {
                var merged = new HashSet<string>(entry.ListingIds, StringComparer.Ordinal);
                foreach (var id in listingIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        merged.Add(id.Trim());
                    }
                }

                entry.ListingIds = merged.ToList();
            }
            version = entry.Version;
            token = entry.Cts.Token;
        }

        _logger.LogInformation(
            "Listing draft sync scheduled for {OwnerId}: debounce={DebounceMs}ms scope={Scope} statuses={Statuses}",
            ownerId,
            (int)wait.TotalMilliseconds,
            resolvedScope,
            string.Join(',', resolved));

        // Detach from the HTTP request — sync runs after the response is already sent.
        _ = Task.Run(() => DebounceAndRunAsync(ownerId, entry, version, token), CancellationToken.None);
    }

    private async Task DebounceAndRunAsync(
        Guid ownerId,
        PendingEntry entry,
        int version,
        CancellationToken token)
    {
        TimeSpan wait;
        lock (entry.Gate)
        {
            if (entry.Version != version) return;
            wait = entry.Debounce;
        }

        if (wait > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(wait, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        else if (token.IsCancellationRequested)
        {
            return;
        }

        List<string> statuses;
        string syncScope;
        List<string> listingIds;
        lock (entry.Gate)
        {
            if (entry.Version != version) return;
            statuses = entry.Statuses.ToList();
            syncScope = entry.SyncScope;
            listingIds = entry.ListingIds.ToList();
        }

        // Refresh presence lease before the pull starts.
        _sellerLease.TouchExtensionLease(ownerId, ExtensionLeaseTtl);

        try
        {
            for (var attempt = 1; attempt <= BusyRetryMaxAttempts; attempt++)
            {
                if (token.IsCancellationRequested) return;
                lock (entry.Gate)
                {
                    if (entry.Version != version) return;
                    statuses = entry.Statuses.ToList();
                    syncScope = entry.SyncScope;
                    listingIds = entry.ListingIds.ToList();
                }

                ListingSyncResultDto result;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var sync = scope.ServiceProvider.GetRequiredService<IListingSyncService>();
                    result = await sync.SyncAsync(
                            new ListingSyncRequest
                            {
                                OwnerUserId = ownerId,
                                Platform = "tiktok",
                                Statuses = statuses,
                                MaxRecordsPerSeller = DraftSyncPageSize,
                                SyncScope = syncScope,
                                ListingIds = listingIds.Count > 0 ? listingIds : null,
                                SyncSource = ListingSyncSources.Extension
                            },
                            progress: null,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }

                if (result.SkippedBusy)
                {
                    _logger.LogInformation(
                        "Listing draft sync busy for {OwnerId}, retry {Attempt}/{Max} in {DelaySeconds}s",
                        ownerId,
                        attempt,
                        BusyRetryMaxAttempts,
                        BusyRetryDelay.TotalSeconds);
                    if (attempt == BusyRetryMaxAttempts)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(BusyRetryDelay, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    _sellerLease.TouchExtensionLease(ownerId, ExtensionLeaseTtl);
                    continue;
                }

                CompletePending(ownerId, entry, version);
                if (result.FailedSellers.Count > 0)
                {
                    _logger.LogWarning(
                        "Listing draft sync (async) partial failure for {OwnerId}: {Message}",
                        ownerId,
                        result.FailedSellers[0].Message);
                }
                else
                {
                    _logger.LogInformation(
                        "Listing draft sync (async) for {OwnerId}: inserted={Inserted}, updated={Updated}",
                        ownerId,
                        result.Inserted,
                        result.Updated);
                }

                return;
            }

            CompletePending(ownerId, entry, version);
            _logger.LogWarning(
                "Listing draft sync gave up after {Max} busy retries for {OwnerId}",
                BusyRetryMaxAttempts,
                ownerId);
        }
        catch (Exception ex)
        {
            CompletePending(ownerId, entry, version);
            _logger.LogWarning(
                ex,
                "Listing draft sync (async) failed for {OwnerId}",
                ownerId);
        }
    }

    private void CompletePending(Guid ownerId, PendingEntry entry, int version)
    {
        lock (entry.Gate)
        {
            if (entry.Version == version)
            {
                _pending.TryRemove(ownerId, out _);
            }
        }
    }

    private static List<string> ResolveStatuses(IReadOnlyList<string>? statuses)
    {
        if (statuses is null || statuses.Count == 0)
        {
            return [ListingSiteStatuses.Draft];
        }

        return statuses
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kv in _pending)
        {
            kv.Value.Cts.Cancel();
            kv.Value.Cts.Dispose();
        }

        _pending.Clear();
    }

    private static string ResolveSyncScope(string? syncScope, IReadOnlyList<string> statuses)
    {
        if (!string.IsNullOrWhiteSpace(syncScope))
        {
            return ListingSyncScopes.Resolve(syncScope, null);
        }

        var onlyDraft = statuses.Count == 1
                        && string.Equals(
                            statuses[0],
                            ListingSiteStatuses.Draft,
                            StringComparison.OrdinalIgnoreCase);
        return onlyDraft ? ListingSyncScopes.Full : ListingSyncScopes.Status;
    }

    private sealed class PendingEntry
    {
        public object Gate { get; } = new();
        public CancellationTokenSource Cts { get; set; } = new();
        public int Version { get; set; }
        public List<string> Statuses { get; set; } = [ListingSiteStatuses.Draft];
        public string SyncScope { get; set; } = ListingSyncScopes.Full;
        public List<string> ListingIds { get; set; } = [];
        public TimeSpan Debounce { get; set; } = DefaultDebounce;
    }
}
