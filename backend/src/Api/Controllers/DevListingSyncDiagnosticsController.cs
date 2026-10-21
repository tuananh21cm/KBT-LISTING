using System.Text.Json;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Helpers;

namespace Api.Controllers;

/// <summary>
/// Probes how 4Seller interprets the listing page <c>updateTimeStart</c> filter.
///
/// This cannot be derived from the code: responses are parsed as naive UTC digits
/// (<c>AppTimeZones.ParseFourSellerNaiveAsUtc</c>) while the create-time filter is sent as Pacific
/// wall-clock, and the two conventions differ by 7-8 hours. Sending the wrong one would silently
/// drop rows from a delta sync, so the convention is measured before the filter is switched on.
///
/// Only available when <c>ASPNETCORE_ENVIRONMENT=Development</c>.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/dev/listing-sync")]
[ApiExplorerSettings(IgnoreApi = true)]
public class DevListingSyncDiagnosticsController : ControllerBase
{
    private const int IdChunkSize = 50;

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IFourSellerApiService _fourSeller;
    private readonly IListingSyncService _listingSync;
    private readonly ICategoryService _categories;
    private readonly ITikTokAttributeRejectionService _rejections;

    /// <summary>Creates the listing sync diagnostics controller.</summary>
    public DevListingSyncDiagnosticsController(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IFourSellerApiService fourSeller,
        IListingSyncService listingSync,
        ICategoryService categories,
        ITikTokAttributeRejectionService rejections)
    {
        _db = db;
        _env = env;
        _fourSeller = fourSeller;
        _listingSync = listingSync;
        _categories = categories;
        _rejections = rejections;
    }

    /// <summary>
    /// One-off repair for listings pinned on <c>UnderReview</c> from before the sync covered every
    /// status bucket. Asks 4Seller about each stuck id directly (<c>listingIdList</c>), re-syncs the
    /// ones that still exist so the normal pipeline resolves their status, and deletes the local rows
    /// for listings 4Seller no longer has at all.
    ///
    /// Deleting is irreversible, so it only happens on evidence that cannot be confused with a broken
    /// request: the id filter is proven against an id 4Seller just returned, and every bucket must
    /// answer successfully. Any failure leaves the seller untouched. Defaults to a dry run.
    /// </summary>
    [HttpPost("reconcile-review")]
    public async Task<IActionResult> ReconcileReview(
        [FromQuery] Guid? ownerId,
        [FromQuery] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var query = _db.Listings.Where(l => l.PublishStatus == PublishStatuses.UnderReview);
        if (ownerId.HasValue)
        {
            query = query.Where(l => l.OwnerId == ownerId.Value);
        }

        var stuck = await query
            .AsNoTracking()
            .Select(l => new { l.Id, l.OwnerId, l.FourSellerListingId, l.Platform })
            .ToListAsync(cancellationToken);

        var report = new List<object>();
        foreach (var group in stuck.GroupBy(l => l.OwnerId))
        {
            var platform = group
                .Select(l => l.Platform)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "tiktok";
            var ids = group.Select(l => l.FourSellerListingId.ToString()).ToList();

            var filterProven = await ProveIdFilterAsync(group.Key, platform, cancellationToken);
            if (!filterProven)
            {
                report.Add(new
                {
                    ownerId = group.Key,
                    stuck = ids.Count,
                    skipped = "không chứng minh được bộ lọc listingIdList — bỏ qua để tránh xoá nhầm"
                });
                continue;
            }

            var found = new HashSet<string>(StringComparer.Ordinal);
            string? bucketError = null;
            foreach (var status in ListingSiteStatuses.DefaultSyncStatuses)
            {
                foreach (var chunk in Chunk(ids, IdChunkSize))
                {
                    try
                    {
                        var page = await _fourSeller.GetListingPageAsync(
                            group.Key,
                            platform,
                            new FourSellerListingPageRequest
                            {
                                PageCurrent = 1,
                                PageSize = 100,
                                Status = status,
                                ListingIds = chunk
                            },
                            cancellationToken);

                        foreach (var record in page.Records)
                        {
                            found.Add(record.Id.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        bucketError = $"{status}: {ex.Message}";
                        break;
                    }
                }

                if (bucketError is not null)
                {
                    break;
                }
            }

            if (bucketError is not null)
            {
                report.Add(new
                {
                    ownerId = group.Key,
                    stuck = ids.Count,
                    skipped = $"một bucket trả lỗi ({bucketError}) — bằng chứng chưa đủ, không xoá gì"
                });
                continue;
            }

            var missing = ids.Where(id => !found.Contains(id)).ToList();
            var resynced = 0;
            var deleted = 0;

            if (!dryRun && found.Count > 0)
            {
                var syncResult = await _listingSync.SyncAsync(
                    new Application.DTOs.Listing.ListingSyncRequest
                    {
                        OwnerUserId = group.Key,
                        Platform = platform,
                        Statuses = ListingSiteStatuses.DefaultSyncStatuses.ToList(),
                        ListingIds = found.ToList(),
                        SyncScope = ListingSyncScopes.Status,
                        SyncSource = ListingSyncSources.Manual
                    },
                    progress: null,
                    cancellationToken);

                resynced = syncResult.Updated;
            }

            if (!dryRun && missing.Count > 0)
            {
                var missingIds = missing.Select(long.Parse).ToHashSet();
                var rows = await _db.Listings
                    .Include(l => l.Variations)
                    .Where(l => l.OwnerId == group.Key
                                && l.PublishStatus == PublishStatuses.UnderReview
                                && missingIds.Contains(l.FourSellerListingId))
                    .ToListAsync(cancellationToken);

                foreach (var row in rows)
                {
                    _db.ListingVariations.RemoveRange(row.Variations);
                }

                _db.Listings.RemoveRange(rows);
                deleted = await _db.SaveChangesAsync(cancellationToken) > 0 ? rows.Count : 0;
            }

            report.Add(new
            {
                ownerId = group.Key,
                stuck = ids.Count,
                stillOnFourSeller = found.Count,
                goneFromFourSeller = missing.Count,
                resynced,
                deleted,
                sampleGone = missing.Take(5).ToList()
            });
        }

        return Ok(new
        {
            dryRun,
            totalStuck = stuck.Count,
            sellers = report,
            note = dryRun
                ? "Chạy lại với dryRun=false để thực sự đồng bộ lại và xoá."
                : "Đã áp dụng."
        });
    }

    /// <summary>
    /// Confirms 4Seller honours <c>listingIdList</c> for this seller right now, by filtering on an id
    /// it just returned. Without this a filter that silently matches nothing would look identical to
    /// every listing having been deleted.
    /// </summary>
    private async Task<bool> ProveIdFilterAsync(
        Guid ownerId,
        string platform,
        CancellationToken cancellationToken)
    {
        try
        {
            var probe = await _fourSeller.GetListingPageAsync(
                ownerId,
                platform,
                new FourSellerListingPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 1,
                    Status = ListingSiteStatuses.Active
                },
                cancellationToken);

            var knownId = probe.Records.FirstOrDefault()?.Id;
            if (knownId is null)
            {
                return false;
            }

            var filtered = await _fourSeller.GetListingPageAsync(
                ownerId,
                platform,
                new FourSellerListingPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 10,
                    Status = ListingSiteStatuses.Active,
                    ListingIds = [knownId.Value.ToString()]
                },
                cancellationToken);

            return filtered.Records.Count > 0
                   && filtered.Records.All(r => r.Id == knownId.Value);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<List<string>> Chunk(List<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }

    /// <summary>
    /// Runs the same listing page request three times — unfiltered, filtered with UTC digits, and
    /// filtered with Pacific digits — and reports what came back for each.
    ///
    /// Read the result as follows: the convention whose oldest returned <c>updateTime</c> is at or
    /// after the digits it sent is the one 4Seller actually applies.
    /// </summary>
    [HttpGet("update-time-probe")]
    public async Task<IActionResult> UpdateTimeProbe(
        [FromQuery] Guid? ownerId,
        [FromQuery] int hoursBack = 24,
        [FromQuery] string status = "draft",
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var seller = await ResolveSellerAsync(ownerId, cancellationToken);
        if (seller is null)
        {
            return BadRequest("Không tìm thấy seller nào có cookie 4Seller đang đăng nhập.");
        }

        var cutoffUtc = DateTime.UtcNow.AddHours(-Math.Abs(hoursBack));
        // Pass the Pacific wall-clock digits through the same (conversion-free) path, so both
        // candidates differ only in the digits that reach 4Seller.
        var cutoffAsPacificDigits = DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeFromUtc(cutoffUtc, AppTimeZones.Pacific),
            DateTimeKind.Utc);

        var baseline = await ProbeAsync(seller.Value, status, null, cancellationToken);
        var utcDigits = await ProbeAsync(seller.Value, status, cutoffUtc, cancellationToken);
        var pacificDigits = await ProbeAsync(seller.Value, status, cutoffAsPacificDigits, cancellationToken);

        return Ok(new
        {
            ownerId = seller.Value,
            status,
            cutoffUtc = Format(cutoffUtc),
            cutoffAsPacificDigits = Format(cutoffAsPacificDigits),
            baseline,
            utcDigits,
            pacificDigits,
            howToRead =
                "Quy ước đúng là quy ước có minUpdateTime >= chính chuỗi nó gửi đi, " +
                "và total nhỏ hơn baseline."
        });
    }

    /// <summary>
    /// Compares unfiltered page totals against the same buckets filtered by
    /// <c>createTime</c> lookback (VN calendar days → Pacific wall-clock), so we can confirm
    /// 4Seller applies the create-date window on published statuses before trusting the
    /// background published loop.
    /// </summary>
    [HttpGet("create-time-probe")]
    public async Task<IActionResult> CreateTimeProbe(
        [FromQuery] Guid? ownerId,
        [FromQuery] int lookbackDays = 10,
        [FromQuery] string? statuses = null,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var seller = await ResolveSellerAsync(ownerId, cancellationToken);
        if (seller is null)
        {
            return BadRequest("Không tìm thấy seller nào có cookie 4Seller đang đăng nhập.");
        }

        var days = Math.Clamp(
            lookbackDays,
            ListingSyncSettingKeys.MinPublishedLookbackDays,
            ListingSyncSettingKeys.MaxPublishedLookbackDays);
        var (dateFromUtc, dateToUtc) = AppTimeZones.GetVietnamLookbackUtcRange(
            days,
            ListingSyncSettingKeys.MaxPublishedLookbackDays);
        var (pacificFrom, pacificTo) = AppTimeZones.ToPacificLocalRange(dateFromUtc, dateToUtc);

        var statusList = string.IsNullOrWhiteSpace(statuses)
            ? new List<string>
            {
                ListingSiteStatuses.Pending,
                ListingSiteStatuses.Active,
                ListingSiteStatuses.Suspended,
                ListingSiteStatuses.Removed
            }
            : statuses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var buckets = new List<object>();
        foreach (var status in statusList)
        {
            buckets.Add(await ProbeCreateTimeAsync(
                seller.Value,
                status,
                dateFromUtc,
                dateToUtc,
                cancellationToken));
        }

        return Ok(new
        {
            ownerId = seller.Value,
            lookbackDays = days,
            dateFromUtc = Format(dateFromUtc),
            dateToUtc = Format(dateToUtc),
            pacificFrom = Format(pacificFrom),
            pacificTo = Format(pacificTo),
            buckets,
            howToRead =
                "Nếu createTime filter hoạt động: total (filtered) < total (baseline) trên bucket có listing cũ hơn lookback. " +
                "Nếu filtered.total == baseline.total trên mọi bucket (và seller có listing cũ) thì 4Seller có thể đang bỏ qua createTime*."
        });
    }

    /// <summary>
    /// Compares local listings stuck at <c>UnderReview</c> against what 4Seller reports for the same
    /// ids right now, so it is possible to tell a genuinely pending TikTok review from a local row
    /// the sync never revisited.
    /// </summary>
    [HttpGet("review-stuck")]
    public async Task<IActionResult> ReviewStuck(
        [FromQuery] Guid? ownerId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var query = _db.Listings
            .AsNoTracking()
            .Where(l => l.PublishStatus == PublishStatuses.UnderReview);

        if (ownerId.HasValue)
        {
            query = query.Where(l => l.OwnerId == ownerId.Value);
        }

        var ageBuckets = await query
            .GroupBy(l => l.OwnerId)
            .Select(g => new
            {
                ownerId = g.Key,
                count = g.Count(),
                oldestPlatformUpdate = g.Min(l => l.PlatformUpdateTime),
                newestPlatformUpdate = g.Max(l => l.PlatformUpdateTime),
                oldestLastSynced = g.Min(l => l.LastSyncedAt),
                newestLastSynced = g.Max(l => l.LastSyncedAt)
            })
            .ToListAsync(cancellationToken);

        var sample = await query
            .OrderBy(l => l.PlatformUpdateTime)
            .Take(Math.Clamp(limit, 1, 50))
            .Select(l => new
            {
                l.OwnerId,
                l.FourSellerListingId,
                l.PageType,
                l.SiteStatus,
                l.PublishStatus,
                l.PlatformUpdateTime,
                l.LastSyncedAt,
                l.SyncError,
                l.Platform
            })
            .ToListAsync(cancellationToken);

        var compared = new List<object>();
        foreach (var group in sample.GroupBy(l => new { l.OwnerId, l.Platform }))
        {
            var ids = group.Select(l => l.FourSellerListingId).ToList();
            var platform = string.IsNullOrWhiteSpace(group.Key.Platform) ? "tiktok" : group.Key.Platform;

            List<FourSellerListingRecordDto> details;
            try
            {
                details = (await _fourSeller.GetListingBatchDetailAsync(
                    group.Key.OwnerId,
                    platform,
                    ids,
                    cancellationToken,
                    allowPerIdFallback: true)).ToList();
            }
            catch (Exception ex)
            {
                compared.Add(new { ownerId = group.Key.OwnerId, error = ex.Message });
                continue;
            }

            var byId = details.ToDictionary(d => d.Id);
            foreach (var local in group)
            {
                byId.TryGetValue(local.FourSellerListingId, out var remote);
                compared.Add(new
                {
                    local.FourSellerListingId,
                    ownerId = local.OwnerId,
                    localPageType = local.PageType,
                    localSiteStatus = local.SiteStatus,
                    localPublishStatus = local.PublishStatus,
                    localPlatformUpdateTime = local.PlatformUpdateTime,
                    localLastSyncedAt = local.LastSyncedAt,
                    localSyncError = local.SyncError,
                    remoteFound = remote is not null,
                    remoteProductStatus = remote?.ProductStatus,
                    remoteErpStatus = remote?.ErpStatus,
                    remotePublishStatus = remote?.PublishStatus,
                    remoteUpdateTime = remote?.UpdateTime,
                    remoteFailedMessage = remote?.FailedMessage
                });
            }
        }

        return Ok(new
        {
            ageBuckets,
            compared,
            howToRead =
                "remoteProductStatus 2 = vẫn đang chờ TikTok duyệt thật. " +
                "4 = đã Active trên 4Seller nhưng local còn UnderReview → sync chưa chạm tới dòng này."
        });
    }

    /// <summary>
    /// Lists 4Seller category attribute schema (for debugging missing Specifics like Condition description).
    /// </summary>
    [HttpGet("category-attrs")]
    public async Task<IActionResult> CategoryAttrs(
        [FromQuery] Guid ownerId,
        [FromQuery] string categoryId,
        [FromQuery] long shopId,
        [FromQuery] string site = "US",
        [FromQuery] string? attributeId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(categoryId) || shopId <= 0)
        {
            return BadRequest("categoryId và shopId là bắt buộc.");
        }

        var attrs = await _fourSeller.GetCategoryAttributeListByIdAsync(
            ownerId,
            categoryId.Trim(),
            shopId,
            string.IsNullOrWhiteSpace(site) ? "US" : site.Trim(),
            cancellationToken);

        var conditionRelated = attrs
            .Where(a =>
                (a.AttributeName ?? string.Empty).Contains("condition", StringComparison.OrdinalIgnoreCase))
            .Select(a => new
            {
                a.AttributeId,
                a.AttributeName,
                a.IsRequired,
                a.IsCustomizable,
                a.RequirementConditionsJson,
                valueCount = a.Values.Count
            })
            .ToList();

        // Full value list of one attribute: the only way to see whether the ids we push still belong
        // to this category's live enum (a rejected "custom value name" means TikTok did not recognise
        // the id we sent for that name).
        var requested = string.IsNullOrWhiteSpace(attributeId)
            ? null
            : attrs
                .Where(a => a.AttributeId == attributeId.Trim())
                .Select(a => new
                {
                    a.AttributeId,
                    a.AttributeName,
                    a.IsRequired,
                    a.IsCustomizable,
                    values = a.Values.Select(v => new { v.ValueId, v.ValueName }).ToList()
                })
                .ToList();

        return Ok(new
        {
            ownerId,
            categoryId,
            shopId,
            site,
            total = attrs.Count,
            conditionRelated,
            requested,
            names = attrs
                .Select(a => new { a.AttributeId, a.AttributeName, a.IsRequired })
                .OrderBy(a => a.AttributeName)
                .ToList()
        });
    }

    /// <summary>
    /// Runs the AI category recommendation exactly as auto publish B2 does and echoes both the form
    /// fields we send and everything we parse back, so a DevTools capture of 4Seller's own UI call can
    /// be compared field by field (that comparison is how a wrong category was traced to the crawl,
    /// not to the AI).
    /// </summary>
    [HttpGet("ai-category")]
    public async Task<IActionResult> AiCategory(
        [FromQuery] Guid ownerId,
        [FromQuery] string title,
        [FromQuery] long? shopId,
        [FromQuery] string site = "US",
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest("title là bắt buộc.");
        }

        var normalizedSite = string.IsNullOrWhiteSpace(site) ? "US" : site.Trim();
        var result = await _categories.RecommendCategoryAsync(
            ownerId,
            new Application.DTOs.Category.RecommendCategoryRequest
            {
                Site = normalizedSite,
                Title = title.Trim(),
                OwnerUserId = ownerId == Guid.Empty ? null : ownerId,
                FourSellerShopId = shopId
            },
            cancellationToken,
            allowOwnerOverride: true);

        return Ok(new
        {
            sentForm = new
            {
                endpoint = "/api/ai/generate-category-tk",
                contentType = "application/x-www-form-urlencoded",
                shopId,
                site = normalizedSite,
                title = title.Trim()
            },
            parsed = new
            {
                result.CategoryId,
                result.CategoryName,
                result.NodePath,
                result.PathIds,
                result.IsLeaf
            },
            catalogCheck = new
            {
                result.IsValidated,
                result.ValidationWarning,
                result.CategoryPath
            }
        });
    }

    /// <summary>
    /// Dry-run of the 4Seller push body for one listing. Runs the real patch → sale-prop rebuild →
    /// sanitize pipeline but sends nothing and saves nothing, so a Color/Size edit can be checked
    /// against the payload before Publish burns a real attempt.
    ///
    /// Returns the resolved axis→option slot map, the root sale-prop maps, and one row per SKU with
    /// its option labels / attrs / SKU id — plus the local variation rows that produced them.
    /// </summary>
    [HttpGet("push-preview/{listingId:guid}")]
    public async Task<IActionResult> PushPreview(
        Guid listingId,
        [FromQuery] bool includePayload = false,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var preview = await _listingSync.BuildPushPreviewAsync(
            listingId,
            includePayload,
            cancellationToken);

        return Ok(preview);
    }

    /// <summary>
    /// The mirrored category tree rows for a set of external ids, plus their ancestors.
    /// <para>
    /// A publish refused for Specifics is often a category problem: TikTok only accepts leaves, and a
    /// non-leaf id makes 4Seller answer with a generic attribute schema whose value ids TikTok then
    /// rejects as custom names.
    /// </para>
    /// </summary>
    [HttpGet("category-nodes")]
    public async Task<IActionResult> CategoryNodes(
        [FromQuery] string ids,
        [FromQuery] string site = "US",
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var wanted = (ids ?? string.Empty)
            .Split([',', '/', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count == 0)
        {
            return BadRequest("ids là bắt buộc.");
        }

        var siteCode = string.IsNullOrWhiteSpace(site) ? "US" : site.Trim();
        var nodes = await _db.TikTokCategories
            .AsNoTracking()
            .Where(c => c.Site == siteCode && wanted.Contains(c.ExternalId))
            .Select(c => new
            {
                c.ExternalId,
                c.Name,
                c.ParentExternalId,
                c.IsLeaf,
                c.Level,
                c.Path,
                c.IsActive,
                c.LastSyncedAt
            })
            .ToListAsync(cancellationToken);

        var childCounts = await _db.TikTokCategories
            .AsNoTracking()
            .Where(c => c.Site == siteCode
                        && c.ParentExternalId != null
                        && wanted.Contains(c.ParentExternalId))
            .GroupBy(c => c.ParentExternalId!)
            .Select(g => new { parent = g.Key, children = g.Count() })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            site = siteCode,
            missing = wanted
                .Where(w => !nodes.Any(n =>
                    string.Equals(n.ExternalId, w, StringComparison.OrdinalIgnoreCase)))
                .ToList(),
            nodes,
            childCounts
        });
    }

    /// <summary>
    /// The Specifics rows a listing's stored payload actually carries, and — for a listing TikTok
    /// accepted — which denylisted values that payload disproves.
    /// <para>
    /// Answers the question the full payload dump buries: whether a value reached TikTok with its
    /// numeric id. A publish refused with "cannot use custom value names" while the row here shows a
    /// valueId means TikTok withdrew the value; no valueId means our mapping never found one.
    /// </para>
    /// </summary>
    [HttpGet("specifics/{listingId:guid}")]
    public async Task<IActionResult> Specifics(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        var listing = await _db.Listings
            .AsNoTracking()
            .Where(l => l.Id == listingId)
            .Select(l => new
            {
                l.Id,
                l.FourSellerListingId,
                l.CategoryId,
                l.PageType,
                l.SiteStatus,
                l.PublishStatus,
                l.SyncError,
                l.RawPayload
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (listing is null)
        {
            return NotFound(new { message = "Không tìm thấy listing." });
        }

        if (string.IsNullOrWhiteSpace(listing.RawPayload))
        {
            return Ok(new { listing.Id, listing.FourSellerListingId, rows = Array.Empty<object>() });
        }

        var rows = new List<object>();
        using var document = JsonDocument.Parse(listing.RawPayload);
        foreach (var field in new[] { "productAttributes", "attributes", "attributeList" })
        {
            if (!document.RootElement.TryGetProperty(field, out var raw))
            {
                continue;
            }

            // 4Seller stores the list either as an array or as a stringified array.
            using var nested = raw.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(raw.GetString() ?? "[]")
                : null;
            var array = nested?.RootElement ?? raw;
            if (array.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var row in array.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                rows.Add(new
                {
                    field,
                    json = row.GetRawText()
                });
            }
        }

        var denylist = await _rejections.ListAsync(cancellationToken);
        var disproves = denylist
            .Where(r => string.Equals(r.CategoryId, listing.CategoryId, StringComparison.OrdinalIgnoreCase))
            .Select(r => new
            {
                r.AttributeId,
                r.ValueName,
                r.Source,
                sentValueId = TikTokAttributeRejectionErrors.ReadSentValueId(
                    listing.RawPayload,
                    r.AttributeId,
                    r.ValueName)
            })
            .ToList();

        return Ok(new
        {
            listing.Id,
            listing.FourSellerListingId,
            listing.CategoryId,
            listing.PageType,
            listing.SiteStatus,
            listing.PublishStatus,
            listing.SyncError,
            rows,
            denylistCheck = disproves
        });
    }

    /// <summary>
    /// Asks 4Seller for one listing id in every status bucket, to find which bucket it currently sits
    /// in. Batch-detail returns nothing for some stale rows, so this decides whether such a row is
    /// genuinely gone from 4Seller or merely moved to a bucket the sync does not reach.
    /// </summary>
    [HttpGet("locate")]
    public async Task<IActionResult> Locate(
        [FromQuery] Guid ownerId,
        [FromQuery] string listingId,
        CancellationToken cancellationToken = default)
    {
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        // Self-test first: an id filter that silently matches nothing looks exactly like a listing that
        // no longer exists, so prove the filter works using an id 4Seller just handed back.
        var probeBucket = await _fourSeller.GetListingPageAsync(
            ownerId,
            "tiktok",
            new FourSellerListingPageRequest { PageCurrent = 1, PageSize = 1, Status = ListingSiteStatuses.Active },
            cancellationToken);

        object? selfTest = null;
        var knownId = probeBucket.Records.FirstOrDefault()?.Id;
        if (knownId is not null)
        {
            var filtered = await _fourSeller.GetListingPageAsync(
                ownerId,
                "tiktok",
                new FourSellerListingPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 10,
                    Status = ListingSiteStatuses.Active,
                    ListingIds = [knownId.ToString()!]
                },
                cancellationToken);

            selfTest = new
            {
                knownId = knownId.ToString(),
                unfilteredTotal = probeBucket.Total,
                filteredTotal = filtered.Total,
                filterWorks = filtered.Records.Any(r =>
                    string.Equals(r.Id.ToString(), knownId.ToString(), StringComparison.Ordinal))
            };
        }

        var found = new List<object>();
        foreach (var status in ListingSiteStatuses.DefaultSyncStatuses)
        {
            try
            {
                var page = await _fourSeller.GetListingPageAsync(
                    ownerId,
                    "tiktok",
                    new FourSellerListingPageRequest
                    {
                        PageCurrent = 1,
                        PageSize = 10,
                        Status = status,
                        ListingIds = [listingId]
                    },
                    cancellationToken);

                var hit = page.Records.FirstOrDefault(r =>
                    string.Equals(r.Id.ToString(), listingId, StringComparison.Ordinal));
                found.Add(new
                {
                    status,
                    total = page.Total,
                    returned = page.Records.Count,
                    matched = hit is not null,
                    productStatus = hit?.ProductStatus,
                    erpStatus = hit?.ErpStatus,
                    updateTime = hit?.UpdateTime
                });
            }
            catch (Exception ex)
            {
                found.Add(new { status, error = ex.Message });
            }
        }

        return Ok(new { ownerId, listingId, selfTest, buckets = found });
    }

    private async Task<object> ProbeAsync(
        Guid ownerId,
        string status,
        DateTime? updatedFrom,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _fourSeller.GetListingPageAsync(
                ownerId,
                "tiktok",
                new FourSellerListingPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 20,
                    Status = status,
                    UpdatedFrom = updatedFrom
                },
                cancellationToken);

            var times = page.Records
                .Select(r => r.UpdateTime)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            return new
            {
                sentStart = updatedFrom is null ? "(none)" : Format(updatedFrom.Value),
                total = page.Total,
                returned = page.Records.Count,
                minUpdateTime = times.FirstOrDefault() ?? "(none)",
                maxUpdateTime = times.LastOrDefault() ?? "(none)"
            };
        }
        catch (Exception ex)
        {
            return new
            {
                sentStart = updatedFrom is null ? "(none)" : Format(updatedFrom.Value),
                error = ex.Message
            };
        }
    }

    private async Task<object> ProbeCreateTimeAsync(
        Guid ownerId,
        string status,
        DateTime dateFromUtc,
        DateTime dateToUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var baseline = await _fourSeller.GetListingPageAsync(
                ownerId,
                "tiktok",
                new FourSellerListingPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 20,
                    Status = status
                },
                cancellationToken);

            var filtered = await _fourSeller.GetListingPageAsync(
                ownerId,
                "tiktok",
                new FourSellerListingPageRequest
                {
                    PageCurrent = 1,
                    PageSize = 20,
                    Status = status,
                    DateFrom = dateFromUtc,
                    DateTo = dateToUtc
                },
                cancellationToken);

            return new
            {
                status,
                baselineTotal = baseline.Total,
                baselineReturned = baseline.Records.Count,
                filteredTotal = filtered.Total,
                filteredReturned = filtered.Records.Count,
                reduced = filtered.Total < baseline.Total
            };
        }
        catch (Exception ex)
        {
            return new { status, error = ex.Message };
        }
    }

    private async Task<Guid?> ResolveSellerAsync(Guid? ownerId, CancellationToken cancellationToken)
    {
        var query = _db.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn);

        if (ownerId.HasValue)
        {
            query = query.Where(u => u.Id == ownerId.Value);
        }

        var id = await query.Select(u => u.Id).FirstOrDefaultAsync(cancellationToken);
        return id == Guid.Empty ? null : id;
    }

    private static string Format(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
