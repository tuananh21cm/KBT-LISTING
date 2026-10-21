using Application.DTOs.FourSeller;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Application.Services;

/// <summary>
/// Scrape ownership over per-user 4Seller crawl (cookie + drafts scoped to the acting user).
/// </summary>
public class ProductScrapeService : IProductScrapeService
{
    /// <summary>Unmatched pending older than this is abandoned so claim stops hitting 4Seller.</summary>
    private static readonly TimeSpan PendingStaleAfter = TimeSpan.FromMinutes(3);

    private readonly IApplicationDbContext _context;
    private readonly IFourSellerApiService _fourSellerApiService;

    /// <summary>
    /// Initializes the product scrape service.
    /// </summary>
    public ProductScrapeService(
        IApplicationDbContext context,
        IFourSellerApiService fourSellerApiService)
    {
        _context = context;
        _fourSellerApiService = fourSellerApiService;
    }

    /// <inheritdoc />
    public async Task<FourSellerCrawlUrlsResponse> SubmitUrlsAsync(
        Guid userId,
        FourSellerCrawlUrlsRequest request,
        CancellationToken cancellationToken = default)
    {
        var cleaned = request.Urls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => SourceUrlKeyHelper.CanonicalizeForCrawl(u))
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
        {
            throw new BusinessException("Danh sách URL scrape không được để trống.");
        }

        var batch = new ScrapeBatch
        {
            CreatedById = userId,
            Status = ScrapeBatchStatuses.Pending,
            SubmittedUrlCount = cleaned.Count
        };

        foreach (var url in cleaned)
        {
            batch.Urls.Add(new ScrapeBatchUrl
            {
                SourceUrl = url.Length > 2000 ? url[..2000] : url,
                SourceUrlKey = SourceUrlKeyHelper.Normalize(url)
            });
        }

        _context.ScrapeBatches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);

        // Drop orphan pending from earlier scrapes so list/poll stops hammering 4Seller.
        await AbandonOtherPendingUrlsAsync(userId, batch.Id, cancellationToken);

        try
        {
            var submitResult = await _fourSellerApiService.SubmitCrawlUrlsAsync(userId, cleaned, cancellationToken);

            // Draft objects in crawl response = already on 4Seller — claim now, do not wait for pool.
            if (submitResult.ExistingDrafts.Count > 0)
            {
                await ClaimRemoteRecordsAsync(
                    userId,
                    batch.Id,
                    submitResult.ExistingDrafts,
                    cancellationToken);
            }

            return new FourSellerCrawlUrlsResponse
            {
                BatchId = batch.Id,
                Accepted = true,
                Count = cleaned.Count,
                FourSellerResponse = submitResult.RawResponse
            };
        }
        catch
        {
            batch.Status = ScrapeBatchStatuses.Failed;
            batch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<FourSellerMoveDraftPageDto> GetDraftsAsync(
        Guid userId,
        bool isAdmin,
        FourSellerMoveDraftPageRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageCurrent = request.PageCurrent < 1 ? 1 : request.PageCurrent;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 200);

        // Scrape poll: claim pending URLs, then return remote drafts matching this batch's URL keys.
        if (request.BatchId.HasValue)
        {
            await ClaimFromFourSellerAsync(userId, request.BatchId, cancellationToken);

            if (request.AbandonPending)
            {
                await AbandonBatchPendingAsync(userId, request.BatchId.Value, markFailed: true, cancellationToken);
            }

            return await ListBatchMatchedRemoteDraftsAsync(
                userId,
                request.BatchId.Value,
                pageCurrent,
                pageSize,
                cancellationToken);
        }

        var ownerFilter = ResolveOwnerFilter(userId, isAdmin, request.MineOnly, request.CreatedById);

        // Admin "Tất cả": local ownership across users (cannot merge multiple cookies in one page).
        if (ownerFilter is null)
        {
            return await ListLocalDraftsAsync(
                userId,
                isAdmin,
                request,
                pageCurrent,
                pageSize,
                cancellationToken);
        }

        // Refresh / list "Của tôi" (or filter 1 user): load trực tiếp từ 4Seller bằng cookie của user đó.
        var cookieUserId = ownerFilter.Value;
        var remote = await _fourSellerApiService.GetMoveDraftPageAsync(
            cookieUserId,
            new FourSellerMoveDraftPageRequest
            {
                PageCurrent = pageCurrent,
                PageSize = pageSize,
                SearchValue = request.SearchValue,
                MineOnly = false
            },
            cancellationToken);

        await UpsertRemoteDraftsAsync(cookieUserId, remote.Records, cancellationToken);

        var ownerName = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == cookieUserId)
            .Select(u => u.FullName ?? u.UserName)
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var record in remote.Records)
        {
            record.OwnedById = cookieUserId;
            record.OwnedByName = ownerName;
        }

        return new FourSellerMoveDraftPageDto
        {
            PageCurrent = remote.PageCurrent > 0 ? remote.PageCurrent : pageCurrent,
            PageSize = remote.PageSize > 0 ? remote.PageSize : pageSize,
            Total = remote.Total,
            Records = remote.Records
        };
    }

    /// <summary>
    /// Loads 4Seller move-drafts for the user and returns rows whose source URL matches the batch.
    /// </summary>
    private async Task<FourSellerMoveDraftPageDto> ListBatchMatchedRemoteDraftsAsync(
        Guid userId,
        Guid batchId,
        int pageCurrent,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var batchUrls = await _context.ScrapeBatchUrls
            .AsNoTracking()
            .Where(u => u.BatchId == batchId && u.Batch.CreatedById == userId)
            .Select(u => new { u.SourceUrlKey, u.MatchedMoveDraftId })
            .ToListAsync(cancellationToken);

        var batchKeys = batchUrls
            .Select(u => u.SourceUrlKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (batchKeys.Count == 0)
        {
            return new FourSellerMoveDraftPageDto
            {
                PageCurrent = pageCurrent,
                PageSize = pageSize,
                Total = 0,
                Records = []
            };
        }

        var remoteRecords = new List<FourSellerMoveDraftRecordDto>();
        var seenIds = new HashSet<long>();
        for (var page = 1; page <= 5; page++)
        {
            var remote = await _fourSellerApiService.GetMoveDraftPageAsync(
                userId,
                new FourSellerMoveDraftPageRequest
                {
                    PageCurrent = page,
                    PageSize = 100,
                    SearchValue = null,
                    MineOnly = false
                },
                cancellationToken);

            foreach (var record in remote.Records)
            {
                if (record.Id > 0 && seenIds.Add(record.Id))
                {
                    remoteRecords.Add(record);
                }
            }

            if (remote.Records.Count < remote.PageSize || remoteRecords.Count >= remote.Total)
            {
                break;
            }
        }

        // Search by product id for keys still missing from the list pages.
        var remoteKeySet = remoteRecords
            .Select(r => SourceUrlKeyHelper.Normalize(r.SourceUrl))
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in batchKeys)
        {
            if (remoteKeySet.Contains(key))
            {
                continue;
            }

            if (!key.StartsWith("p:", StringComparison.OrdinalIgnoreCase) || key.Length < 3)
            {
                continue;
            }

            var found = await _fourSellerApiService.GetMoveDraftPageAsync(
                userId,
                new FourSellerMoveDraftPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 50,
                    SearchValue = key[2..],
                    MineOnly = false
                },
                cancellationToken);

            foreach (var record in found.Records)
            {
                if (record.Id > 0 && seenIds.Add(record.Id))
                {
                    remoteRecords.Add(record);
                    var nk = SourceUrlKeyHelper.Normalize(record.SourceUrl);
                    if (!string.IsNullOrEmpty(nk))
                    {
                        remoteKeySet.Add(nk);
                    }
                }
            }
        }

        var matched = remoteRecords
            .Where(r => batchKeys.Contains(SourceUrlKeyHelper.Normalize(r.SourceUrl)))
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .OrderByDescending(r => r.CreateTime ?? string.Empty)
            .ToList();

        await UpsertRemoteDraftsAsync(userId, matched, cancellationToken);
        await AttachBatchToMatchedDraftsAsync(userId, batchId, matched, cancellationToken);

        var ownerName = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FullName ?? u.UserName)
            .FirstOrDefaultAsync(cancellationToken);

        foreach (var record in matched)
        {
            record.OwnedById = userId;
            record.OwnedByName = ownerName;
        }

        await RefreshBatchStatusAsync(batchId, forceFailedIfUnmatched: false, cancellationToken);

        return new FourSellerMoveDraftPageDto
        {
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            Total = matched.Count,
            Records = matched
                .Skip((pageCurrent - 1) * pageSize)
                .Take(pageSize)
                .ToList()
        };
    }

    /// <summary>
    /// Links matched remote drafts to the scrape batch (BatchId + MatchedMoveDraftId).
    /// </summary>
    private async Task AttachBatchToMatchedDraftsAsync(
        Guid userId,
        Guid batchId,
        IReadOnlyList<FourSellerMoveDraftRecordDto> matched,
        CancellationToken cancellationToken)
    {
        if (matched.Count == 0)
        {
            return;
        }

        var pendingUrls = await _context.ScrapeBatchUrls
            .Include(u => u.Batch)
            .Where(u => u.BatchId == batchId && u.Batch.CreatedById == userId)
            .ToListAsync(cancellationToken);

        var pendingByKey = pendingUrls
            .Where(u => !string.IsNullOrEmpty(u.SourceUrlKey))
            .GroupBy(u => u.SourceUrlKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var matchedIds = matched.Select(m => m.Id).Where(id => id > 0).Distinct().ToList();
        var locals = await _context.ScrapeDrafts
            .Where(d => d.CreatedById == userId && matchedIds.Contains(d.FourSellerMoveDraftId))
            .ToDictionaryAsync(d => d.FourSellerMoveDraftId, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var record in matched)
        {
            var key = SourceUrlKeyHelper.Normalize(record.SourceUrl);
            if (string.IsNullOrEmpty(key) || !pendingByKey.TryGetValue(key, out var urlRows))
            {
                continue;
            }

            var urlRow = urlRows.FirstOrDefault(u => u.MatchedMoveDraftId == null) ?? urlRows[0];
            urlRow.MatchedMoveDraftId = record.Id;
            urlRow.UpdatedAt = now;

            if (locals.TryGetValue(record.Id, out var draft))
            {
                draft.BatchId = batchId;
                draft.LastSeenAt = now;
                draft.UpdatedAt = now;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<FourSellerMoveDraftPageDto> ListLocalDraftsAsync(
        Guid userId,
        bool isAdmin,
        FourSellerMoveDraftPageRequest request,
        int pageCurrent,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var ownerFilter = ResolveOwnerFilter(userId, isAdmin, request.MineOnly, request.CreatedById);

        var query = _context.ScrapeDrafts
            .AsNoTracking()
            .Include(d => d.CreatedBy)
            .AsQueryable();

        if (ownerFilter.HasValue)
        {
            query = query.Where(d => d.CreatedById == ownerFilter.Value);
        }

        if (request.BatchId.HasValue)
        {
            query = query.Where(d => d.BatchId == request.BatchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchValue))
        {
            var search = request.SearchValue.Trim();
            query = query.Where(d =>
                (d.ProductName != null && d.ProductName.Contains(search)) ||
                (d.SourceUrl != null && d.SourceUrl.Contains(search)) ||
                (d.Creator != null && d.Creator.Contains(search)));
        }

        var total = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(d => d.LastSeenAt)
            .ThenByDescending(d => d.CreatedAt)
            .Skip((pageCurrent - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (request.BatchId.HasValue)
        {
            await RefreshBatchStatusAsync(request.BatchId.Value, forceFailedIfUnmatched: false, cancellationToken);
        }

        return new FourSellerMoveDraftPageDto
        {
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            Total = total,
            Records = records.Select(MapDraft).ToList()
        };
    }

    /// <summary>
    /// Upserts 4Seller move-draft page rows into local ScrapeDrafts for the cookie owner.
    /// </summary>
    private async Task UpsertRemoteDraftsAsync(
        Guid userId,
        IReadOnlyList<FourSellerMoveDraftRecordDto> remoteRecords,
        CancellationToken cancellationToken)
    {
        var ids = remoteRecords.Where(r => r.Id > 0).Select(r => r.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var existing = await _context.ScrapeDrafts
            .Where(d => d.CreatedById == userId && ids.Contains(d.FourSellerMoveDraftId))
            .ToDictionaryAsync(d => d.FourSellerMoveDraftId, cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var record in remoteRecords.Where(r => r.Id > 0))
        {
            var key = SourceUrlKeyHelper.Normalize(record.SourceUrl);
            if (existing.TryGetValue(record.Id, out var local))
            {
                local.ProductName = record.ProductName ?? local.ProductName;
                local.ImageUrl = record.Image ?? local.ImageUrl;
                local.Status = record.Status;
                local.VariantCount = record.VariantCount;
                local.LowPrice = record.LowPrice;
                local.HighPrice = record.HighPrice;
                local.Currency = record.Currency;
                local.Creator = record.Creator;
                local.FourSellerCreateTime = record.CreateTime;
                local.SourceUrl = record.SourceUrl ?? local.SourceUrl;
                if (!string.IsNullOrEmpty(key))
                {
                    local.SourceUrlKey = key;
                }

                local.LastSeenAt = now;
                local.UpdatedAt = now;
            }
            else
            {
                _context.ScrapeDrafts.Add(new ScrapeDraft
                {
                    CreatedById = userId,
                    FourSellerMoveDraftId = record.Id,
                    SourceUrl = record.SourceUrl,
                    SourceUrlKey = string.IsNullOrEmpty(key) ? record.Id.ToString() : key,
                    ProductName = record.ProductName,
                    ImageUrl = record.Image,
                    Status = record.Status,
                    VariantCount = record.VariantCount,
                    LowPrice = record.LowPrice,
                    HighPrice = record.HighPrice,
                    Currency = record.Currency,
                    Creator = record.Creator,
                    FourSellerCreateTime = record.CreateTime,
                    LastSeenAt = now
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FourSellerSaveToPlatformResponse> SaveToPlatformAsync(
        Guid userId,
        bool isAdmin,
        FourSellerSaveToPlatformRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureOwnedDraftsAsync(userId, isAdmin, request.MoveDraftId, cancellationToken);
        return await _fourSellerApiService.SaveToPlatformAsync(userId, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteDraftsAsync(
        Guid userId,
        bool isAdmin,
        FourSellerDeleteDraftsRequest request,
        CancellationToken cancellationToken = default)
    {
        var ids = request.MoveDraftIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một draft để xóa.");
        }

        await EnsureOwnedDraftsAsync(userId, isAdmin, ids, cancellationToken);
        await _fourSellerApiService.DeleteMoveDraftsAsync(userId, ids, cancellationToken);

        var localsQuery = _context.ScrapeDrafts
            .Where(d => ids.Contains(d.FourSellerMoveDraftId));
        if (!isAdmin)
        {
            localsQuery = localsQuery.Where(d => d.CreatedById == userId);
        }

        var locals = await localsQuery.ToListAsync(cancellationToken);

        if (locals.Count > 0)
        {
            _context.ScrapeDrafts.RemoveRange(locals);
        }

        // Keep MatchedMoveDraftId so batch URLs do not re-enter pending and force 4Seller poll storms.
        // Re-scrape creates a new batch with fresh pending rows.
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<FourSellerScheduleProgressDto> GetScheduleAsync(
        Guid userId,
        string scheduleCode,
        CancellationToken cancellationToken = default) =>
        _fourSellerApiService.GetScheduleAsync(userId, scheduleCode, cancellationToken);

    private static Guid? ResolveOwnerFilter(Guid userId, bool isAdmin, bool mineOnly, Guid? createdById)
    {
        if (!isAdmin)
        {
            return userId;
        }

        if (createdById.HasValue)
        {
            return createdById.Value;
        }

        if (mineOnly)
        {
            return userId;
        }

        // Admin + mineOnly false + no createdById → all users
        return null;
    }

    private async Task EnsureOwnedDraftsAsync(
        Guid userId,
        bool isAdmin,
        IReadOnlyList<long> moveDraftIds,
        CancellationToken cancellationToken)
    {
        var ids = moveDraftIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một draft.");
        }

        if (isAdmin)
        {
            return;
        }

        var ownedCount = await _context.ScrapeDrafts
            .CountAsync(
                d => d.CreatedById == userId && ids.Contains(d.FourSellerMoveDraftId),
                cancellationToken);

        if (ownedCount != ids.Count)
        {
            throw new BusinessException("Một hoặc nhiều draft không thuộc quyền của bạn.");
        }
    }

    private async Task ClaimFromFourSellerAsync(
        Guid userId,
        Guid? batchId,
        CancellationToken cancellationToken)
    {
        await AbandonStalePendingUrlsAsync(userId, cancellationToken);

        var pendingUrlsQuery = _context.ScrapeBatchUrls
            .Include(u => u.Batch)
            .Where(u => u.MatchedMoveDraftId == null && u.Batch.CreatedById == userId);

        if (batchId.HasValue)
        {
            pendingUrlsQuery = pendingUrlsQuery.Where(u => u.BatchId == batchId.Value);
        }
        else
        {
            var since = DateTime.UtcNow - PendingStaleAfter;
            pendingUrlsQuery = pendingUrlsQuery.Where(u => u.Batch.CreatedAt >= since);
        }

        var pendingUrls = await pendingUrlsQuery.ToListAsync(cancellationToken);
        if (pendingUrls.Count == 0)
        {
            return;
        }

        // 4Seller often returns data:[] then never creates the draft — kick crawl once after ~20s.
        if (batchId.HasValue)
        {
            await MaybeResubmitUnmatchedBatchAsync(userId, batchId.Value, pendingUrls, cancellationToken);
            // Reload pending after possible immediate claim from resubmit drafts.
            pendingUrls = await pendingUrlsQuery.ToListAsync(cancellationToken);
            if (pendingUrls.Count == 0)
            {
                return;
            }
        }

        var remoteRecords = new List<FourSellerMoveDraftRecordDto>();
        var seenIds = new HashSet<long>();
        for (var page = 1; page <= 5; page++)
        {
            var remote = await _fourSellerApiService.GetMoveDraftPageAsync(
                userId,
                new FourSellerMoveDraftPageRequest
                {
                    PageCurrent = page,
                    PageSize = 100,
                    SearchValue = null,
                    MineOnly = false
                },
                cancellationToken);

            foreach (var record in remote.Records)
            {
                if (record.Id > 0 && seenIds.Add(record.Id))
                {
                    remoteRecords.Add(record);
                }
            }

            if (remote.Records.Count < remote.PageSize || remoteRecords.Count >= remote.Total)
            {
                break;
            }
        }

        // Search by product id when list page miss (new drafts can be buried past the first pages).
        var pendingKeys = pendingUrls
            .Select(u => u.SourceUrlKey)
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remoteKeySet = remoteRecords
            .Select(r => SourceUrlKeyHelper.Normalize(r.SourceUrl))
            .Where(k => !string.IsNullOrEmpty(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in pendingKeys)
        {
            if (remoteKeySet.Contains(key))
            {
                continue;
            }

            if (!key.StartsWith("p:", StringComparison.OrdinalIgnoreCase) || key.Length < 3)
            {
                continue;
            }

            var productId = key[2..];
            var found = await _fourSellerApiService.GetMoveDraftPageAsync(
                userId,
                new FourSellerMoveDraftPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 50,
                    SearchValue = productId,
                    MineOnly = false
                },
                cancellationToken);

            foreach (var record in found.Records)
            {
                if (record.Id > 0 && seenIds.Add(record.Id))
                {
                    remoteRecords.Add(record);
                    var nk = SourceUrlKeyHelper.Normalize(record.SourceUrl);
                    if (!string.IsNullOrEmpty(nk))
                    {
                        remoteKeySet.Add(nk);
                    }
                }
            }
        }

        if (remoteRecords.Count == 0)
        {
            return;
        }

        await ClaimRemoteRecordsAsync(userId, batchId, remoteRecords, cancellationToken);
    }

    /// <summary>
    /// Re-POSTs pending URLs once when move-draft still has no match after ~20s
    /// (4Seller accepts data:[] but sometimes never materializes the draft).
    /// </summary>
    private async Task MaybeResubmitUnmatchedBatchAsync(
        Guid userId,
        Guid batchId,
        List<ScrapeBatchUrl> pendingUrls,
        CancellationToken cancellationToken)
    {
        var batch = await _context.ScrapeBatches
            .FirstOrDefaultAsync(b => b.Id == batchId && b.CreatedById == userId, cancellationToken);
        if (batch is null || batch.Status != ScrapeBatchStatuses.Pending)
        {
            return;
        }

        var age = DateTime.UtcNow - batch.CreatedAt;
        if (age < TimeSpan.FromSeconds(20))
        {
            return;
        }

        var urls = pendingUrls
            .Select(u => u.SourceUrl)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (urls.Count == 0)
        {
            return;
        }

        // Mark before call so concurrent poll does not double-submit.
        batch.Status = ScrapeBatchStatuses.Retrying;
        batch.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);


        try
        {
            var result = await _fourSellerApiService.SubmitCrawlUrlsAsync(userId, urls, cancellationToken);

            if (result.ExistingDrafts.Count > 0)
            {
                await ClaimRemoteRecordsAsync(userId, batchId, result.ExistingDrafts, cancellationToken);
            }
        }
        catch
        {
            // Keep Retrying so we do not loop; FE timeout will abandon.
        }
    }

    /// <summary>
    /// Marks unmatched batch URLs older than <see cref="PendingStaleAfter"/> as abandoned so claim polling stops.
    /// </summary>
    private async Task AbandonStalePendingUrlsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - PendingStaleAfter;
        var stale = await _context.ScrapeBatchUrls
            .Include(u => u.Batch)
            .Where(u =>
                u.MatchedMoveDraftId == null &&
                u.Batch.CreatedById == userId &&
                u.Batch.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var batchIds = new HashSet<Guid>();
        foreach (var url in stale)
        {
            url.MatchedMoveDraftId = 0;
            url.UpdatedAt = now;
            batchIds.Add(url.BatchId);
        }

        await _context.SaveChangesAsync(cancellationToken);
        foreach (var id in batchIds)
        {
            await RefreshBatchStatusAsync(id, forceFailedIfUnmatched: true, cancellationToken);
        }
    }

    /// <summary>
    /// Abandons unmatched URLs for one batch (exit poll pool). Optionally marks batch Failed.
    /// </summary>
    private async Task AbandonBatchPendingAsync(
        Guid userId,
        Guid batchId,
        bool markFailed,
        CancellationToken cancellationToken)
    {
        var pending = await _context.ScrapeBatchUrls
            .Include(u => u.Batch)
            .Where(u =>
                u.BatchId == batchId &&
                u.MatchedMoveDraftId == null &&
                u.Batch.CreatedById == userId)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var url in pending)
        {
            url.MatchedMoveDraftId = 0;
            url.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await RefreshBatchStatusAsync(batchId, forceFailedIfUnmatched: markFailed, cancellationToken);
    }

    /// <summary>
    /// Abandons unmatched URLs from other batches when starting a new scrape (clears poll storm).
    /// </summary>
    private async Task AbandonOtherPendingUrlsAsync(
        Guid userId,
        Guid keepBatchId,
        CancellationToken cancellationToken)
    {
        var others = await _context.ScrapeBatchUrls
            .Include(u => u.Batch)
            .Where(u =>
                u.MatchedMoveDraftId == null &&
                u.Batch.CreatedById == userId &&
                u.BatchId != keepBatchId)
            .ToListAsync(cancellationToken);

        if (others.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var url in others)
        {
            url.MatchedMoveDraftId = 0;
            url.UpdatedAt = now;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Claims remote move-draft records against this user's pending batch URLs (per-user cookie pool).
    /// </summary>
    private async Task ClaimRemoteRecordsAsync(
        Guid userId,
        Guid? batchId,
        IReadOnlyList<FourSellerMoveDraftRecordDto> remoteRecords,
        CancellationToken cancellationToken)
    {
        if (remoteRecords.Count == 0)
        {
            return;
        }

        var pendingUrlsQuery = _context.ScrapeBatchUrls
            .Include(u => u.Batch)
            .Where(u => u.MatchedMoveDraftId == null && u.Batch.CreatedById == userId);

        if (batchId.HasValue)
        {
            pendingUrlsQuery = pendingUrlsQuery.Where(u => u.BatchId == batchId.Value);
        }

        var pendingUrls = await pendingUrlsQuery.ToListAsync(cancellationToken);
        if (pendingUrls.Count == 0)
        {
            return;
        }

        var ownedByMoveId = await _context.ScrapeDrafts
            .Where(d => d.CreatedById == userId)
            .ToDictionaryAsync(d => d.FourSellerMoveDraftId, cancellationToken);
        var claimedIds = ownedByMoveId.Keys.ToHashSet();

        var pendingByKey = pendingUrls
            .Where(u => !string.IsNullOrEmpty(u.SourceUrlKey))
            .GroupBy(u => u.SourceUrlKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var touchedBatchIds = new HashSet<Guid>();

        foreach (var record in remoteRecords.OrderByDescending(r => r.CreateTime ?? string.Empty))
        {
            if (record.Id <= 0)
            {
                continue;
            }

            var key = SourceUrlKeyHelper.Normalize(record.SourceUrl);
            if (string.IsNullOrEmpty(key) || !pendingByKey.TryGetValue(key, out var urlRows))
            {
                continue;
            }

            var urlRow = batchId.HasValue
                ? urlRows.FirstOrDefault(u => u.BatchId == batchId.Value) ?? urlRows[0]
                : urlRows[0];

            if (ownedByMoveId.TryGetValue(record.Id, out var owned))
            {
                owned.ProductName = record.ProductName ?? owned.ProductName;
                owned.ImageUrl = record.Image ?? owned.ImageUrl;
                owned.Status = record.Status;
                owned.VariantCount = record.VariantCount;
                owned.LowPrice = record.LowPrice;
                owned.HighPrice = record.HighPrice;
                owned.Currency = record.Currency;
                owned.Creator = record.Creator;
                owned.FourSellerCreateTime = record.CreateTime;
                owned.SourceUrl = record.SourceUrl ?? owned.SourceUrl;
                owned.BatchId = urlRow.BatchId;
                owned.LastSeenAt = now;
                owned.UpdatedAt = now;

                urlRow.MatchedMoveDraftId = record.Id;
                urlRow.UpdatedAt = now;
                touchedBatchIds.Add(urlRow.BatchId);

                urlRows.Remove(urlRow);
                if (urlRows.Count == 0)
                {
                    pendingByKey.Remove(key);
                }

                continue;
            }

            var draft = new ScrapeDraft
            {
                CreatedById = userId,
                FourSellerMoveDraftId = record.Id,
                BatchId = urlRow.BatchId,
                SourceUrl = record.SourceUrl,
                SourceUrlKey = key,
                ProductName = record.ProductName,
                ImageUrl = record.Image,
                Status = record.Status,
                VariantCount = record.VariantCount,
                LowPrice = record.LowPrice,
                HighPrice = record.HighPrice,
                Currency = record.Currency,
                Creator = record.Creator,
                FourSellerCreateTime = record.CreateTime,
                LastSeenAt = now
            };

            _context.ScrapeDrafts.Add(draft);
            claimedIds.Add(record.Id);
            ownedByMoveId[record.Id] = draft;

            urlRow.MatchedMoveDraftId = record.Id;
            urlRow.UpdatedAt = now;
            touchedBatchIds.Add(urlRow.BatchId);

            urlRows.Remove(urlRow);
            if (urlRows.Count == 0)
            {
                pendingByKey.Remove(key);
            }
        }

        var ownedRemote = remoteRecords.Where(r => claimedIds.Contains(r.Id)).ToList();
        if (ownedRemote.Count > 0)
        {
            var ownedIds = ownedRemote.Select(r => r.Id).ToHashSet();
            var locals = await _context.ScrapeDrafts
                .Where(d => d.CreatedById == userId && ownedIds.Contains(d.FourSellerMoveDraftId))
                .ToListAsync(cancellationToken);

            var byId = ownedRemote.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var local in locals)
            {
                if (!byId.TryGetValue(local.FourSellerMoveDraftId, out var remote))
                {
                    continue;
                }

                local.ProductName = remote.ProductName;
                local.ImageUrl = remote.Image;
                local.Status = remote.Status;
                local.VariantCount = remote.VariantCount;
                local.LowPrice = remote.LowPrice;
                local.HighPrice = remote.HighPrice;
                local.Currency = remote.Currency;
                local.Creator = remote.Creator;
                local.FourSellerCreateTime = remote.CreateTime;
                local.SourceUrl = remote.SourceUrl ?? local.SourceUrl;
                local.LastSeenAt = now;
                local.UpdatedAt = now;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var id in touchedBatchIds)
        {
            await RefreshBatchStatusAsync(id, forceFailedIfUnmatched: false, cancellationToken);
        }
    }

    private async Task RefreshBatchStatusAsync(
        Guid batchId,
        bool forceFailedIfUnmatched,
        CancellationToken cancellationToken)
    {
        var batch = await _context.ScrapeBatches
            .Include(b => b.Urls)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);
        if (batch is null)
        {
            return;
        }

        // MatchedMoveDraftId > 0 = claimed; 0 = abandoned; null = still waiting.
        var claimed = batch.Urls.Count(u => u.MatchedMoveDraftId is > 0);
        var unresolved = batch.Urls.Count(u => u.MatchedMoveDraftId == null);

        if (unresolved == 0 && claimed > 0)
        {
            batch.Status = ScrapeBatchStatuses.Ready;
        }
        else if (forceFailedIfUnmatched || (unresolved == 0 && claimed == 0))
        {
            batch.Status = ScrapeBatchStatuses.Failed;
        }
        // Mark batch Retrying so mid-poll does not treat as first Pending for re-submit.
        else if (batch.Status != ScrapeBatchStatuses.Failed &&
                 batch.Status != ScrapeBatchStatuses.Retrying)
        {
            batch.Status = ScrapeBatchStatuses.Pending;
        }

        batch.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static FourSellerMoveDraftRecordDto MapDraft(ScrapeDraft d) => new()
    {
        Id = d.FourSellerMoveDraftId,
        Status = d.Status,
        Image = d.ImageUrl,
        SourceUrl = d.SourceUrl,
        VariantCount = d.VariantCount,
        LowPrice = d.LowPrice,
        HighPrice = d.HighPrice,
        Currency = d.Currency,
        Creator = d.Creator,
        CreateTime = d.FourSellerCreateTime,
        ProductName = d.ProductName,
        OwnedById = d.CreatedById,
        OwnedByName = d.CreatedBy?.FullName ?? d.CreatedBy?.UserName
    };

}
