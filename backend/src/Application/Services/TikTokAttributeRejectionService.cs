using System.Text.Json;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <inheritdoc cref="ITikTokAttributeRejectionService" />
public class TikTokAttributeRejectionService : ITikTokAttributeRejectionService
{
    private const string CacheKeyPrefix = "tiktok-attr-rejections";
    private const int SampleErrorMaxLength = 1000;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Bumped on every new rejection so all cached category snapshots go stale at once; a per-key
    /// remove cannot reach the snapshots of other categories after an all-categories row lands.
    /// </summary>
    private static long _generation;

    private readonly IApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TikTokAttributeRejectionService> _logger;

    /// <summary>Creates the rejection learning service.</summary>
    public TikTokAttributeRejectionService(
        IApplicationDbContext db,
        IMemoryCache cache,
        ILogger<TikTokAttributeRejectionService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TikTokAttributeValueDenylist> GetDenylistAsync(
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = (categoryId ?? string.Empty).Trim();
        var cacheKey = $"{CacheKeyPrefix}:{Interlocked.Read(ref _generation)}:{category}";
        if (_cache.TryGetValue(cacheKey, out TikTokAttributeValueDenylist? cached) && cached is not null)
        {
            return cached;
        }

        // Rows kept only for diagnosis must never block a value: doing so drops the whole attribute
        // and turns "custom value" into "missing required Specifics" on the next publish. Two kinds
        // are diagnosis-only — we sent the value without a numeric valueId, and the value is still in
        // use by listings live on TikTok in the same category.
        var query = _db.TikTokAttributeValueRejections
            .AsNoTracking()
            .Where(r =>
                r.Source != TikTokAttributeRejectionSources.MissingValueId
                && r.Source != TikTokAttributeRejectionSources.ContradictedByLive);
        query = category.Length > 0
            ? query.Where(r =>
                r.CategoryId == TikTokAttributeValueRejection.AllCategories || r.CategoryId == category)
            : query.Where(r => r.CategoryId == TikTokAttributeValueRejection.AllCategories);

        var rows = await query
            .Select(r => new { r.AttributeId, r.ValueName })
            .ToListAsync(cancellationToken);

        var denylist = new TikTokAttributeValueDenylist(rows.Select(r => (r.AttributeId, r.ValueName)));
        _cache.Set(cacheKey, denylist, CacheTtl);
        return denylist;
    }

    /// <inheritdoc />
    public async Task<bool> RecordFromErrorAsync(
        Guid? listingId,
        string? categoryId,
        string? error,
        CancellationToken cancellationToken = default)
    {
        if (!TikTokAttributeRejectionErrors.TryParseCustomValueRejection(
                error,
                out var attributeId,
                out var attributeName,
                out var valueName))
        {
            return false;
        }

        var valueNameNorm = ListingSpecificsMappingEngine.NormalizeKey(valueName);
        if (valueNameNorm.Length == 0)
        {
            return false;
        }

        // Without a category the value would have to be blocked everywhere, and the same attribute id
        // exposes a different enum per category, so that would block values still valid elsewhere.
        var category = (categoryId ?? string.Empty).Trim();
        if (category.Length == 0)
        {
            _logger.LogWarning(
                "TikTok rejected attribute {AttributeId} value {ValueName} but listing {ListingId} has no category; not learning it",
                attributeId,
                valueName,
                listingId);
            return false;
        }

        var existing = await _db.TikTokAttributeValueRejections
            .FirstOrDefaultAsync(
                r => r.CategoryId == category
                     && r.AttributeId == attributeId
                     && r.ValueNameNorm == valueNameNorm,
                cancellationToken);

        // Settled knowledge, left alone: a value an admin blocked on purpose, and one blocked for every
        // category (which this per-category row would only shadow).
        if (existing?.Source == TikTokAttributeRejectionSources.Seed
            || (existing is null
                && (await GetDenylistAsync(category, cancellationToken)).IsRejected(attributeId, valueName)))
        {
            return false;
        }

        var now = DateTime.UtcNow;

        // "Cannot use custom value names" says one thing only: the value reached TikTok as bare text.
        // 4Seller forwards bare text when our row gave it no id it could resolve, so this message is
        // always a defect in the payload we built — never proof that TikTok retired the value.
        //
        // Treating it as proof poisoned category 903824: five of the six Washing instructions values
        // ended up blocked while a listing published in a sibling category with 1001195 (Hand wash
        // only) went live the same night. The row was written with values[] alone, missing the flat
        // attrValue/valueId mirror 4Seller reads, so every value we tried came back "custom".
        //
        // So it is recorded for diagnosis and never blocks. A value genuinely gone from the enum needs
        // no denylist either: canonicalization drops what the live schema no longer offers.
        var sentValueId = await ResolveSentValueIdAsync(listingId, attributeId, valueName, cancellationToken);
        const string source = TikTokAttributeRejectionSources.MissingValueId;

        // A failed listing keeps its SyncError and every sync pass hands the same message over again.
        // Same listing, same verdict, nothing new: writing again would count sync passes as failures.
        if (existing is not null
            && existing.Source == source
            && existing.SampleListingId == listingId)
        {
            return false;
        }

        var sample = Truncate(error, SampleErrorMaxLength);
        if (existing is null)
        {
            _db.TikTokAttributeValueRejections.Add(new TikTokAttributeValueRejection
            {
                CategoryId = category,
                AttributeId = attributeId,
                AttributeName = Truncate(attributeName, 200),
                ValueId = sentValueId,
                ValueName = Truncate(valueName, 300) ?? string.Empty,
                ValueNameNorm = valueNameNorm,
                RejectionCount = 1,
                FirstSeenAt = now,
                LastSeenAt = now,
                Source = source,
                SampleListingId = listingId,
                SampleError = sample
            });
        }
        else
        {
            existing.RejectionCount += 1;
            existing.LastSeenAt = now;
            existing.SampleListingId = listingId ?? existing.SampleListingId;
            existing.SampleError = sample ?? existing.SampleError;
            if (string.IsNullOrWhiteSpace(existing.AttributeName))
            {
                existing.AttributeName = Truncate(attributeName, 200);
            }

            existing.ValueId ??= sentValueId;

            // A row an earlier build promoted to blocking is demoted here: the same message that
            // created it never justified blocking in the first place.
            existing.Source = source;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Two shops can fail on the same value at once; the unique index decides the winner and
            // the loser has nothing left to learn.
            _logger.LogInformation(
                ex,
                "TikTok rejection for attribute {AttributeId} value {ValueName} already recorded",
                attributeId,
                valueName);
            InvalidateCache();
            return true;
        }

        InvalidateCache();
        _logger.LogWarning(
            "TikTok từ chối attribute {AttributeId} ({AttributeName}) value {ValueName} trên category "
            + "{CategoryId} vì nhận được dưới dạng custom value name (valueId trong payload lúc đó: "
            + "{ValueId}). Ghi nhận để chẩn đoán, KHÔNG chặn giá trị: lỗi nằm ở payload thiếu id, "
            + "không phải TikTok gỡ giá trị khỏi enum.",
            attributeId,
            attributeName,
            valueName,
            category,
            sentValueId ?? "(không có)");
        return true;
    }

    /// <inheritdoc />
    public async Task<int> RecordAcceptedAsync(
        Guid? listingId,
        string? categoryId,
        string? rawPayload,
        CancellationToken cancellationToken = default)
    {
        var category = (categoryId ?? string.Empty).Trim();
        if (category.Length == 0 || string.IsNullOrWhiteSpace(rawPayload))
        {
            return 0;
        }

        // Only rows that still block: the diagnosis-only sources are already out of the denylist.
        var blocking = await _db.TikTokAttributeValueRejections
            .Where(r =>
                r.CategoryId == category
                && r.Source != TikTokAttributeRejectionSources.MissingValueId
                && r.Source != TikTokAttributeRejectionSources.ContradictedByLive)
            .ToListAsync(cancellationToken);
        if (blocking.Count == 0)
        {
            return 0;
        }

        var cleared = 0;
        foreach (var row in blocking)
        {
            string? sentValueId;
            try
            {
                sentValueId = TikTokAttributeRejectionErrors.ReadSentValueId(
                    rawPayload,
                    row.AttributeId,
                    row.ValueName);
            }
            catch (JsonException)
            {
                return 0;
            }

            if (sentValueId is null)
            {
                continue;
            }

            row.Source = TikTokAttributeRejectionSources.ContradictedByLive;
            row.ValueId ??= sentValueId;
            cleared++;
            _logger.LogWarning(
                "Bỏ chặn {AttributeId} ({AttributeName}) = {ValueName} (valueId {ValueId}) ở category "
                + "{CategoryId}: listing {ListingId} đã lên sàn với đúng giá trị này, nên lần từ chối "
                + "trước là lỗi payload chứ không phải TikTok gỡ giá trị.",
                row.AttributeId,
                row.AttributeName,
                row.ValueName,
                sentValueId,
                category,
                listingId);
        }

        if (cleared > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            InvalidateCache();
        }

        return cleared;
    }

    /// <summary>
    /// Reads back the <c>valueId</c> the failed publish actually sent for the rejected value.
    /// Returns null when the payload carried only a name (or cannot be read), which means the
    /// rejection proves nothing about TikTok's enum.
    /// </summary>
    private async Task<string?> ResolveSentValueIdAsync(
        Guid? listingId,
        string attributeId,
        string valueName,
        CancellationToken cancellationToken)
    {
        if (listingId is not Guid id || id == Guid.Empty)
        {
            return null;
        }

        var rawPayload = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => l.RawPayload)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        try
        {
            return TikTokAttributeRejectionErrors.ReadSentValueId(rawPayload, attributeId, valueName);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(
                ex,
                "Không đọc được RawPayload của listing {ListingId} để lấy valueId đã gửi",
                id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<TikTokAttributeValueRejectionDto>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await _db.TikTokAttributeValueRejections
            .AsNoTracking()
            .OrderByDescending(r => r.LastSeenAt)
            .Select(r => new TikTokAttributeValueRejectionDto
            {
                Id = r.Id,
                CategoryId = r.CategoryId,
                AttributeId = r.AttributeId,
                AttributeName = r.AttributeName,
                ValueId = r.ValueId,
                ValueName = r.ValueName,
                RejectionCount = r.RejectionCount,
                FirstSeenAt = r.FirstSeenAt,
                LastSeenAt = r.LastSeenAt,
                Source = r.Source,
                SampleListingId = r.SampleListingId,
                SampleError = r.SampleError
            })
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await _db.TikTokAttributeValueRejections
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (row is null)
        {
            return false;
        }

        _db.TikTokAttributeValueRejections.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        InvalidateCache();
        return true;
    }

    private void InvalidateCache() => Interlocked.Increment(ref _generation);

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
