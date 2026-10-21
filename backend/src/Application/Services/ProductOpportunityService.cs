using System.Globalization;
using System.Text.Json;
using Application.DTOs.ProductOpportunity;
using Application.DTOs.Shop;
using Application.Interfaces;
using Application.Validators.ProductOpportunity;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Product-opportunity snapshot sync (extension) and paged list (admin/seller).
/// </summary>
public class ProductOpportunityService : IProductOpportunityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;
    private readonly ITikTokShopSyncLogWriter _tikTokSyncLog;

    /// <summary>Creates the service.</summary>
    public ProductOpportunityService(
        IApplicationDbContext db,
        ISettingProvider settings,
        ITikTokShopSyncLogWriter tikTokSyncLog)
    {
        _db = db;
        _settings = settings;
        _tikTokSyncLog = tikTokSyncLog;
    }

    /// <inheritdoc />
    public async Task<ProductOpportunitySyncConfigDto> GetSyncConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var values = await _settings.GetValuesAsync(
            [
                ExtensionTikTokOpportunitySyncSettingKeys.Enabled,
                ExtensionTikTokOpportunitySyncSettingKeys.IntervalValue,
                ExtensionTikTokOpportunitySyncSettingKeys.IntervalUnit
            ],
            cancellationToken);

        var enabled = values.TryGetValue(ExtensionTikTokOpportunitySyncSettingKeys.Enabled, out var e)
            && string.Equals(e?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        var intervalValue = ExtensionTikTokOpportunitySyncSettingKeys.DefaultIntervalValue;
        if (values.TryGetValue(ExtensionTikTokOpportunitySyncSettingKeys.IntervalValue, out var v)
            && int.TryParse(v, out var parsed)
            && parsed >= 1)
        {
            intervalValue = parsed;
        }

        var unit = values.TryGetValue(ExtensionTikTokOpportunitySyncSettingKeys.IntervalUnit, out var u)
            ? (u ?? string.Empty).Trim()
            : ExtensionTikTokOpportunitySyncSettingKeys.UnitMinutes;

        var isSeconds = string.Equals(unit, ExtensionTikTokOpportunitySyncSettingKeys.UnitSeconds, StringComparison.OrdinalIgnoreCase)
            || string.Equals(unit, "second", StringComparison.OrdinalIgnoreCase);
        // Typo "minute" / unknown unit → minutes (default seed), không rơi xuống giây.
        var seconds = isSeconds ? intervalValue : intervalValue * 60;

        return new ProductOpportunitySyncConfigDto
        {
            Enabled = enabled,
            IntervalSeconds = Math.Max(1, seconds)
        };
    }

    /// <inheritdoc />
    public async Task<ProductOpportunitySyncContextDto> GetSyncContextAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken = default)
    {
        var shop = await ResolveOwnedShopAsync(ownerId, externalShopId, cancellationToken);
        var resolved = await ResolveCategoryInfosAsync(shop, cancellationToken);
        return new ProductOpportunitySyncContextDto
        {
            ShopId = shop.Id,
            HasCatalog = resolved.Groups.Count > 0,
            CategoryInfos = resolved.Groups.Count > 0 ? resolved.Groups[0] : [],
            CategoryInfoGroups = resolved.Groups,
            CatalogNames = resolved.CatalogNames
        };
    }

    /// <inheritdoc />
    public async Task<SyncProductOpportunitiesResultDto> SyncAsync(
        Guid ownerId,
        SyncProductOpportunitiesDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = SyncProductOpportunitiesValidator.ResolveExternalShopId(dto);
        Guid? shopId = null;
        try
        {
        var shop = await ResolveOwnedShopAsync(
            ownerId,
            externalShopId,
            cancellationToken);

        shopId = shop.Id;

        var resolved = await ResolveCategoryInfosAsync(shop, cancellationToken);
        if (resolved.Groups.Count == 0)
        {
            throw new BusinessException(
                "Shop chưa gán danh mục niche — không ghi đè snapshot.");
        }

        var now = DateTime.UtcNow;
        var payloadByLeadId = new Dictionary<string, SyncProductOpportunityItemDto>(StringComparer.Ordinal);
        foreach (var item in dto.Items ?? [])
        {
            var leadId = (item.LeadId ?? string.Empty).Trim();
            if (leadId.Length == 0 || !payloadByLeadId.TryAdd(leadId, item))
            {
                continue;
            }
        }

        var payloadLeadIds = payloadByLeadId.Keys.ToList();
        var byLeadId = await LoadKeywordsByLeadIdsAsync(payloadLeadIds, cancellationToken);
        var preexisting = byLeadId.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var leadId in payloadLeadIds)
        {
            if (byLeadId.TryGetValue(leadId, out var row))
            {
                ApplyItem(row, payloadByLeadId[leadId], now, isNew: false);
                continue;
            }

            row = new ProductOpportunity
            {
                LeadId = Truncate(leadId, 64),
                CreatedAt = now
            };
            ApplyItem(row, payloadByLeadId[leadId], now, isNew: true);
            await _db.ProductOpportunities.AddAsync(row, cancellationToken);
            byLeadId[leadId] = row;
        }

        await FlushKeywordWritesAsync(
            payloadLeadIds,
            payloadByLeadId,
            byLeadId,
            now,
            cancellationToken);

        var desiredIds = payloadLeadIds.Select(id => byLeadId[id].Id).ToList();
        var existingLinks = await _db.ProductOpportunityShopLinks
            .Include(l => l.ProductOpportunity)
            .Where(l => l.ShopId == shop.Id)
            .ToListAsync(cancellationToken);
        var diff = ProductOpportunitySyncApply.DiffLinks(
            existingLinks.Select(l => l.ProductOpportunityId).ToList(),
            desiredIds);

        var savedRows = await _db.ProductOpportunitySavedKeywords
            .AsNoTracking()
            .Where(s => s.ShopId == shop.Id)
            .Select(s => new { s.KeywordNormalized, s.LeadId })
            .ToListAsync(cancellationToken);
        var savedKeys = savedRows
            .Select(s => s.KeywordNormalized)
            .ToHashSet(StringComparer.Ordinal);
        var savedLeadIds = savedRows
            .Select(s => (s.LeadId ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var opportunityById = existingLinks.ToDictionary(
            l => l.ProductOpportunityId,
            l => (l.ProductOpportunity.LeadId, l.ProductOpportunity.LeadName));
        var toRemoveIds = ProductOpportunitySyncApply.FilterUnlinkPreservingSaved(
            diff.ToRemove,
            opportunityById,
            savedKeys,
            savedLeadIds);
        var removeSet = toRemoveIds.ToHashSet();
        foreach (var link in existingLinks)
        {
            if (!removeSet.Contains(link.ProductOpportunityId))
            {
                continue;
            }

            _db.ProductOpportunityShopLinks.Remove(link);
        }

        foreach (var opportunityId in diff.ToAdd)
        {
            await _db.ProductOpportunityShopLinks.AddAsync(
                new ProductOpportunityShopLink
                {
                    ShopId = shop.Id,
                    ProductOpportunityId = opportunityId,
                    CreatedAt = now
                },
                cancellationToken);
        }

        shop.ProductOpportunitiesSyncedAt = now;
        shop.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await PruneOrphanKeywordsAsync(toRemoveIds, cancellationToken);

        var inserted = payloadLeadIds.Count(id =>
            !preexisting.Contains(id) && byLeadId[id].CreatedAt == now);
        var preservedSaved = diff.ToRemove.Count - toRemoveIds.Count;

        await _tikTokSyncLog.WriteAsync(
            ownerId,
            dto.RunId,
            externalShopId,
            shop.Id,
            TikTokShopSyncTypes.Opportunity,
            TikTokShopSyncStatuses.Completed,
            cancellationToken: cancellationToken);

        return new SyncProductOpportunitiesResultDto
        {
            ShopId = shop.Id,
            Inserted = inserted,
            Linked = desiredIds.Count + preservedSaved,
            Skipped = payloadLeadIds.Count - inserted
        };
        }
        catch (Exception ex)
        {
            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shopId,
                TikTokShopSyncTypes.Opportunity,
                TikTokShopSyncStatuses.Failed,
                ex.Message,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ProductOpportunityListPageDto> ListAsync(
        ProductOpportunityListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 100);
        var pageCurrent = query.Page ?? query.PageCurrent;
        if (pageCurrent < 1)
        {
            pageCurrent = 1;
        }

        var shopId = query.ShopId;
        var ownerId = query.OwnerId;
        var rows = _db.ProductOpportunities.AsNoTracking().AsQueryable();
        rows = FilterLinkedKeywords(rows, shopId, ownerId);

        var keyword = query.Q?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            var needle = keyword.ToLowerInvariant();
            rows = rows.Where(x => x.LeadName.ToLower().Contains(needle));
        }

        if (query.Saved == true)
        {
            if (shopId.HasValue)
            {
                var sid = shopId.Value;
                rows = rows.Where(x => _db.ProductOpportunitySavedKeywords.Any(s =>
                    s.ShopId == sid && s.KeywordNormalized == x.LeadName.ToLower()));
            }
            else if (ownerId.HasValue)
            {
                var oid = ownerId.Value;
                rows = rows.Where(x => _db.ProductOpportunitySavedKeywords.Any(s =>
                    s.OwnerId == oid
                    && s.KeywordNormalized == x.LeadName.ToLower()
                    && x.ShopLinks.Any(l => l.ShopId == s.ShopId)));
            }
            else
            {
                rows = rows.Where(x => _db.ProductOpportunitySavedKeywords.Any(s =>
                    s.KeywordNormalized == x.LeadName.ToLower()
                    && x.ShopLinks.Any(l => l.ShopId == s.ShopId)));
            }
        }

        var optionRows = rows;
        var leadTags = NormalizeLeadTags(query.LeadTags, query.LeadTag);
        if (leadTags.Count > 0)
        {
            rows = FilterByLeadTags(rows, leadTags);
        }

        var sort = (query.Sort ?? "volume").Trim().ToLowerInvariant();
        var sortDirRaw = query.SortDir?.Trim().ToLowerInvariant();
        var desc = sortDirRaw switch
        {
            "asc" => false,
            "desc" => true,
            _ => sort != "name"
        };

        rows = sort switch
        {
            "name" => desc
                ? rows.OrderByDescending(x => x.LeadName).ThenByDescending(x => x.SearchVolume)
                : rows.OrderBy(x => x.LeadName).ThenByDescending(x => x.SearchVolume),
            "synced" => desc
                ? rows
                    .OrderByDescending(x => x.ShopLinks
                        .Where(l => (!shopId.HasValue || l.ShopId == shopId.Value)
                            && (!ownerId.HasValue || l.Shop.OwnerId == ownerId.Value))
                        .Max(l => l.Shop.ProductOpportunitiesSyncedAt))
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName)
                : rows
                    .OrderBy(x => x.ShopLinks
                        .Where(l => (!shopId.HasValue || l.ShopId == shopId.Value)
                            && (!ownerId.HasValue || l.Shop.OwnerId == ownerId.Value))
                        .Max(l => l.Shop.ProductOpportunitiesSyncedAt))
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName),
            "online" => desc
                ? rows
                    .OrderByDescending(x => x.OnlineProducts.HasValue)
                    .ThenByDescending(x => x.OnlineProducts)
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName)
                : rows
                    .OrderByDescending(x => x.OnlineProducts.HasValue)
                    .ThenBy(x => x.OnlineProducts)
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName),
            "order" => desc
                ? rows
                    .OrderByDescending(x => x.OrderCount.HasValue)
                    .ThenByDescending(x => x.OrderCount)
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName)
                : rows
                    .OrderByDescending(x => x.OrderCount.HasValue)
                    .ThenBy(x => x.OrderCount)
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName),
            "gmv" => desc
                ? rows
                    .OrderByDescending(x => x.Gmv.HasValue)
                    .ThenByDescending(x => x.Gmv)
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName)
                : rows
                    .OrderByDescending(x => x.Gmv.HasValue)
                    .ThenBy(x => x.Gmv)
                    .ThenByDescending(x => x.SearchVolume)
                    .ThenBy(x => x.LeadName),
            _ => desc
                ? rows.OrderByDescending(x => x.SearchVolume).ThenBy(x => x.LeadName)
                : rows.OrderBy(x => x.SearchVolume).ThenBy(x => x.LeadName)
        };

        var snapshotTotal = await rows.CountAsync(cancellationToken);
        var savedOnlyItems = await LoadSavedOnlyListItemsAsync(
            shopId,
            ownerId,
            keyword,
            leadTags,
            rows,
            cancellationToken);
        var total = snapshotTotal + savedOnlyItems.Count;

        var shops = _db.Shops.AsNoTracking().AsQueryable();
        if (shopId.HasValue)
        {
            shops = shops.Where(s => s.Id == shopId.Value);
        }

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            shops = shops.Where(s => s.OwnerId == oid);
        }

        DateTime? lastSyncedAt = null;
        if (await shops.AnyAsync(cancellationToken))
        {
            lastSyncedAt = await shops.MaxAsync(
                s => (DateTime?)s.ProductOpportunitiesSyncedAt,
                cancellationToken);
        }

        var skip = (pageCurrent - 1) * pageSize;
        var snapshotSkip = Math.Min(skip, snapshotTotal);
        var snapshotTake = Math.Min(pageSize, Math.Max(0, snapshotTotal - snapshotSkip));
        var pageRows = await rows
            .Skip(snapshotSkip)
            .Take(snapshotTake)
            .Select(x => new
            {
                x.Id,
                x.LeadId,
                x.LeadName,
                x.PicUrl,
                x.PicUrls,
                x.SearchVolume,
                x.OnlineProducts,
                x.OnlineProductsRaw,
                x.SearchVolumeRingRatio,
                x.OnlineProductsRingRatio,
                x.OrderCount,
                x.OrderRaw,
                x.OrderRingRatio,
                x.Gmv,
                x.GmvRaw,
                x.GmvRingRatio,
                x.Categories,
                x.Level1CateId,
                x.Level1CateName,
                x.Level2CateId,
                x.Level2CateName,
                x.Level3CateId,
                x.Level3CateName,
                x.LeadTagTexts,
                x.LeadSourceTag,
                x.LeadClusterSourceTag,
                x.OpportunityType
            })
            .ToListAsync(cancellationToken);

        var shopsByKeyword = await LoadScopedShopInfoAsync(
            pageRows.Select(x => x.Id).ToList(),
            shopId,
            ownerId,
            cancellationToken);

        var savedShopIds = shopId.HasValue
            ? new List<Guid> { shopId.Value }
            : shopsByKeyword.Values.SelectMany(s => s.LinkedShopIds).Distinct().ToList();

        var savedKeys = await LoadSavedKeysAsync(
            savedShopIds,
            pageRows.Select(x => x.LeadName).ToList(),
            cancellationToken);

        var leadTagOptions = await LoadLeadTagOptionsAsync(optionRows, cancellationToken);

        var items = pageRows.Select(x =>
        {
            shopsByKeyword.TryGetValue(x.Id, out var shop);
            var itemOwnerId = ownerId ?? shop?.OwnerId ?? Guid.Empty;
            return new ProductOpportunityListItemDto
            {
                Id = x.Id,
                ShopId = shop?.ShopId ?? Guid.Empty,
                ShopName = shop?.ShopName ?? string.Empty,
                ShopProfileName = shop?.ShopProfileName,
                OwnerId = itemOwnerId,
                OwnerFullName = shop?.OwnerFullName,
                OwnerUserName = shop?.OwnerUserName,
                LeadId = x.LeadId,
                LeadName = x.LeadName,
                PicUrl = x.PicUrl,
                PicUrls = DeserializeStringList(x.PicUrls),
                SearchVolume = x.SearchVolume,
                OnlineProducts = x.OnlineProducts,
                OnlineProductsRaw = x.OnlineProductsRaw,
                SearchVolumeRingRatio = x.SearchVolumeRingRatio,
                OnlineProductsRingRatio = x.OnlineProductsRingRatio,
                OrderCount = x.OrderCount,
                OrderRaw = x.OrderRaw,
                OrderRingRatio = x.OrderRingRatio,
                Gmv = x.Gmv,
                GmvRaw = x.GmvRaw,
                GmvRingRatio = x.GmvRingRatio,
                Categories = DeserializeCategories(x.Categories),
                Level1CateId = x.Level1CateId,
                Level1CateName = x.Level1CateName,
                Level2CateId = x.Level2CateId,
                Level2CateName = x.Level2CateName,
                Level3CateId = x.Level3CateId,
                Level3CateName = x.Level3CateName,
                LeadTagTexts = DeserializeStringList(x.LeadTagTexts),
                LeadSourceTag = x.LeadSourceTag,
                LeadClusterSourceTag = x.LeadClusterSourceTag,
                OpportunityType = x.OpportunityType,
                ShopSyncedAt = shop?.ShopSyncedAt,
                ShopCount = shop?.ShopCount ?? 0,
                IsSaved = shop is not null
                    && ProductOpportunitySavedApply.IsSavedForShops(
                        savedKeys,
                        shop.LinkedShopIds,
                        x.LeadName)
            };
        }).ToList();

        var extraSkip = Math.Max(0, skip - snapshotTotal);
        var extraTake = pageSize - items.Count;
        if (extraTake > 0 && extraSkip < savedOnlyItems.Count)
        {
            items.AddRange(savedOnlyItems.Skip(extraSkip).Take(extraTake));
        }

        return new ProductOpportunityListPageDto
        {
            Items = items,
            Total = total,
            PageCurrent = pageCurrent,
            PageSize = pageSize,
            LastSyncedAt = lastSyncedAt,
            LeadTagOptions = leadTagOptions
        };
    }

    /// <inheritdoc />
    public async Task<BulkDeleteProductOpportunitiesResultDto> BulkDeleteAsync(
        IReadOnlyList<Guid> ids,
        Guid? ownerId,
        Guid? shopId = null,
        CancellationToken cancellationToken = default)
    {
        var distinctIds = (ids ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(201)
            .ToList();
        if (distinctIds.Count == 0)
        {
            throw new BusinessException("Chọn ít nhất một keyword để xóa.");
        }

        if (distinctIds.Count > 200)
        {
            throw new BusinessException("Tối đa 200 keyword mỗi lần xóa.");
        }

        if (shopId.HasValue || ownerId.HasValue)
        {
            return await UnlinkKeywordsAsync(
                distinctIds,
                ownerId,
                shopId,
                cancellationToken);
        }

        var rows = await _db.ProductOpportunities
            .Where(x => distinctIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            throw new BusinessException("Không có keyword nào để xóa.");
        }

        var links = await _db.ProductOpportunityShopLinks
            .Where(l => distinctIds.Contains(l.ProductOpportunityId))
            .Select(l => new { l.Id, l.ShopId, OwnerId = l.Shop.OwnerId, l.ProductOpportunity.LeadName })
            .ToListAsync(cancellationToken);
        var deleted = links
            .Select(l => (l.ShopId, l.OwnerId, Key: NormalizeKeyword(l.LeadName)))
            .Where(x => x.Key.Length > 0)
            .ToList();
        var removedLinkIds = links.Select(l => l.Id).ToHashSet();

        _db.ProductOpportunities.RemoveRange(rows);
        await CleanupKeywordSideEffectsAsync(deleted, removedLinkIds, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return new BulkDeleteProductOpportunitiesResultDto { Deleted = rows.Count };
    }

    /// <inheritdoc />
    public async Task<ProductOpportunitySavedResultDto> SetSavedAsync(
        Guid opportunityId,
        bool saved,
        Guid? ownerScope,
        Guid savedByUserId,
        Guid? shopId = null,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.ProductOpportunities
            .FirstOrDefaultAsync(x => x.Id == opportunityId, cancellationToken);
        if (row is null)
        {
            throw new NotFoundException("Không tìm thấy keyword.");
        }

        var shop = await ResolveBookmarkShopAsync(
            opportunityId,
            shopId,
            ownerScope,
            cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy keyword.");

        var normalized = NormalizeKeyword(row.LeadName);
        if (normalized.Length == 0)
        {
            throw new BusinessException("Keyword trống — không lưu được.");
        }

        var existing = await _db.ProductOpportunitySavedKeywords
            .FirstOrDefaultAsync(
                x => x.ShopId == shop.Id && x.KeywordNormalized == normalized,
                cancellationToken);

        if (saved)
        {
            if (existing is null)
            {
                await _db.ProductOpportunitySavedKeywords.AddAsync(
                    new ProductOpportunitySavedKeyword
                    {
                        OwnerId = shop.OwnerId,
                        ShopId = shop.Id,
                        Keyword = Truncate(row.LeadName.Trim(), 500),
                        KeywordNormalized = Truncate(normalized, 500),
                        LeadId = TruncateOptional(NormalizeOptional(row.LeadId), 64),
                        PicUrl = TruncateOptional(NormalizeOptional(row.PicUrl), 2000),
                        SavedByUserId = savedByUserId,
                        CreatedAt = DateTime.UtcNow
                    },
                    cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (existing.PicUrl != row.PicUrl || existing.LeadId != row.LeadId)
            {
                existing.Keyword = Truncate(row.LeadName.Trim(), 500);
                existing.LeadId = TruncateOptional(NormalizeOptional(row.LeadId), 64);
                existing.PicUrl = TruncateOptional(NormalizeOptional(row.PicUrl), 2000);
                existing.SavedByUserId = savedByUserId;
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new ProductOpportunitySavedResultDto { Saved = true };
        }

        if (existing is not null)
        {
            _db.ProductOpportunitySavedKeywords.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new ProductOpportunitySavedResultDto { Saved = false };
    }

    /// <inheritdoc />
    public async Task<ProductOpportunitySavedResultDto> SetSavedByKeywordAsync(
        Guid shopId,
        string keyword,
        bool saved,
        Guid? ownerScope,
        Guid savedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (shopId == Guid.Empty)
        {
            throw new BusinessException("shopId không hợp lệ.");
        }

        var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == shopId);
        if (ownerScope.HasValue)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == ownerScope.Value);
        }

        var shop = await shopQuery
            .Select(s => new { s.Id, s.OwnerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var trimmed = (keyword ?? string.Empty).Trim();
        var normalized = NormalizeKeyword(trimmed);
        if (normalized.Length == 0)
        {
            throw new BusinessException("Keyword trống — không lưu được.");
        }

        var existing = await _db.ProductOpportunitySavedKeywords
            .FirstOrDefaultAsync(
                x => x.ShopId == shop.Id && x.KeywordNormalized == normalized,
                cancellationToken);

        if (saved)
        {
            if (existing is null)
            {
                await _db.ProductOpportunitySavedKeywords.AddAsync(
                    new ProductOpportunitySavedKeyword
                    {
                        OwnerId = shop.OwnerId,
                        ShopId = shop.Id,
                        Keyword = Truncate(trimmed, 500),
                        KeywordNormalized = Truncate(normalized, 500),
                        SavedByUserId = savedByUserId,
                        CreatedAt = DateTime.UtcNow
                    },
                    cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new ProductOpportunitySavedResultDto { Saved = true };
        }

        if (existing is not null)
        {
            _db.ProductOpportunitySavedKeywords.Remove(existing);
        }

        var snapshotNames = await _db.ProductOpportunityShopLinks
            .AsNoTracking()
            .Where(l => l.ShopId == shop.Id)
            .Select(l => l.ProductOpportunity.LeadName)
            .ToListAsync(cancellationToken);
        var hasSnapshot = snapshotNames.Any(n => NormalizeKeyword(n) == normalized);
        var pruned = false;
        if (!hasSnapshot)
        {
            pruned = await PruneShopResearchAsync(
                new Dictionary<Guid, HashSet<string>>
                {
                    [shop.Id] = new HashSet<string>(StringComparer.Ordinal) { normalized }
                },
                cancellationToken);
        }

        if (existing is not null || pruned)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new ProductOpportunitySavedResultDto { Saved = false };
    }

    /// <inheritdoc />
    public async Task<SavedKeywordListDto> ListSavedKeywordsAsync(
        Guid shopId,
        Guid? ownerScope,
        CancellationToken cancellationToken = default)
    {
        if (shopId == Guid.Empty)
        {
            throw new BusinessException("shopId không hợp lệ.");
        }

        var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == shopId);
        if (ownerScope.HasValue)
        {
            shopQuery = shopQuery.Where(s => s.OwnerId == ownerScope.Value);
        }

        var shop = await shopQuery
            .Select(s => new { s.Id, s.OwnerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy shop.");

        var saved = await _db.ProductOpportunitySavedKeywords
            .AsNoTracking()
            .Where(s => s.ShopId == shop.Id)
            .ToListAsync(cancellationToken);

        var savedKeys = saved
            .Select(s => s.KeywordNormalized)
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var snapshotByKey = new Dictionary<string, (string? PicUrl, string? LeadTagTexts, int SearchVolume)>(
            StringComparer.Ordinal);
        if (savedKeys.Count > 0)
        {
            var snapshotRows = await _db.ProductOpportunities
                .AsNoTracking()
                .Where(x =>
                    x.ShopLinks.Any(l => l.ShopId == shopId)
                    && x.LeadName != null
                    && savedKeys.Contains(x.LeadName.ToLower()))
                .Select(x => new { x.LeadName, x.PicUrl, x.LeadTagTexts, x.SearchVolume })
                .ToListAsync(cancellationToken);
            foreach (var row in snapshotRows)
            {
                var key = NormalizeKeyword(row.LeadName);
                if (key.Length == 0 || snapshotByKey.ContainsKey(key))
                {
                    continue;
                }

                snapshotByKey[key] = (row.PicUrl, row.LeadTagTexts, row.SearchVolume);
            }
        }

        ShopKeywordResearchDto? research = null;
        try
        {
            var researchJson = await _db.ShopKeywordResearches
                .AsNoTracking()
                .Where(x => x.ShopId == shopId)
                .Select(x => x.PayloadJson)
                .FirstOrDefaultAsync(cancellationToken);
            research = ShopKeywordResearchPersistence.Deserialize(researchJson);
        }
        catch
        {
            research = null;
        }

        var researchByKey = ShopKeywordResearchPersistence.IndexKeywordNiche(research);

        var items = saved
            .Select(s =>
            {
                snapshotByKey.TryGetValue(s.KeywordNormalized, out var snap);
                researchByKey.TryGetValue(s.KeywordNormalized, out var niche);
                return new SavedKeywordItemDto
                {
                    Keyword = s.Keyword,
                    PicUrl = snap.PicUrl ?? s.PicUrl,
                    LeadTagTexts = DeserializeStringList(snap.LeadTagTexts),
                    SearchVolume = snap.SearchVolume,
                    Rationale = niche?.Rationale,
                    Fit = niche?.Fit,
                    ResearchLane = niche?.Lane
                };
            })
            .GroupBy(x => NormalizeKeyword(x.Keyword), StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(x => x.SearchVolume)
            .ThenBy(x => x.Keyword, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToList();

        foreach (var item in items)
        {
            try
            {
                researchByKey.TryGetValue(NormalizeKeyword(item.Keyword), out var niche);
                KeywordMatchPhrases.Apply(item, niche?.MustTokens);
            }
            catch
            {
                // Keep the keyword even if phrase stamping fails for one row.
            }
        }

        return new SavedKeywordListDto { Items = items };
    }

    private async Task<HashSet<(Guid ShopId, string Keyword)>> LoadSavedKeysAsync(
        IReadOnlyList<Guid> shopIds,
        IReadOnlyList<string> leadNames,
        CancellationToken cancellationToken)
    {
        var distinctShops = shopIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var names = leadNames
            .Select(NormalizeKeyword)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        if (distinctShops.Count == 0 || names.Count == 0)
        {
            return [];
        }

        var saved = await _db.ProductOpportunitySavedKeywords
            .AsNoTracking()
            .Where(s => distinctShops.Contains(s.ShopId) && names.Contains(s.KeywordNormalized))
            .Select(s => new { s.ShopId, s.KeywordNormalized })
            .ToListAsync(cancellationToken);

        return saved.Select(s => (s.ShopId, s.KeywordNormalized)).ToHashSet();
    }

    /// <summary>
    /// Drop bookmarks of shops that no longer link the keyword, and strip those
    /// keywords from the affected shops' «Shop này bán gì» JSON. One SaveChanges at the caller.
    /// </summary>
    private async Task CleanupKeywordSideEffectsAsync(
        IReadOnlyList<(Guid ShopId, Guid OwnerId, string Key)> unlinked,
        IReadOnlySet<Guid> removedLinkIds,
        CancellationToken cancellationToken)
    {
        if (unlinked.Count == 0)
        {
            return;
        }

        var shopIds = unlinked.Select(x => x.ShopId).Distinct().ToList();
        var keys = unlinked.Select(x => x.Key).Distinct().ToList();

        var remaining = await _db.ProductOpportunityShopLinks
            .AsNoTracking()
            .Where(l => shopIds.Contains(l.ShopId) && !removedLinkIds.Contains(l.Id))
            .Select(l => new { l.ShopId, l.ProductOpportunity.LeadName })
            .ToListAsync(cancellationToken);

        var remainingByShop = remaining
            .Select(x => (x.ShopId, Key: NormalizeKeyword(x.LeadName)))
            .Where(x => x.Key.Length > 0)
            .ToHashSet();

        var saved = await _db.ProductOpportunitySavedKeywords
            .Where(s => shopIds.Contains(s.ShopId) && keys.Contains(s.KeywordNormalized))
            .ToListAsync(cancellationToken);
        foreach (var row in saved)
        {
            if (ProductOpportunitySavedApply.ShouldDropBookmark(
                    row.ShopId,
                    row.KeywordNormalized,
                    remainingByShop))
            {
                _db.ProductOpportunitySavedKeywords.Remove(row);
            }
        }

        var pruneByShop = new Dictionary<Guid, HashSet<string>>();
        foreach (var item in unlinked)
        {
            if (remainingByShop.Contains((item.ShopId, item.Key)))
            {
                continue;
            }

            if (!pruneByShop.TryGetValue(item.ShopId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                pruneByShop[item.ShopId] = set;
            }

            set.Add(item.Key);
        }

        await PruneShopResearchAsync(pruneByShop, cancellationToken);
    }

    /// <summary>
    /// Rewrite <c>ShopKeywordResearches.PayloadJson</c> without the given keywords.
    /// Keeps shop-level analysis / weather / categories. Returns true when a row changed.
    /// </summary>
    private async Task<bool> PruneShopResearchAsync(
        IReadOnlyDictionary<Guid, HashSet<string>> keysByShop,
        CancellationToken cancellationToken)
    {
        if (keysByShop.Count == 0)
        {
            return false;
        }

        var shopIds = keysByShop.Keys.ToList();
        var researches = await _db.ShopKeywordResearches
            .Where(x => shopIds.Contains(x.ShopId))
            .ToListAsync(cancellationToken);
        if (researches.Count == 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var row in researches)
        {
            if (!keysByShop.TryGetValue(row.ShopId, out var keys) || keys.Count == 0)
            {
                continue;
            }

            var next = ShopKeywordResearchPersistence.TryRemoveKeywords(row.PayloadJson, keys);
            if (next is null)
            {
                continue;
            }

            row.PayloadJson = next;
            row.UpdatedAt = now;
            changed = true;
        }

        return changed;
    }

    private static string NormalizeKeyword(string? name) =>
        (name ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Bookmarks saved from «Shop này bán gì» (audience suggest) that have no TikTok snapshot
    /// link for this shop. Extension Search already lists them; the admin/seller table must too.
    /// </summary>
    private async Task<List<ProductOpportunityListItemDto>> LoadSavedOnlyListItemsAsync(
        Guid? shopId,
        Guid? ownerId,
        string? keyword,
        IReadOnlyList<string> leadTags,
        IQueryable<ProductOpportunity> snapshotRows,
        CancellationToken cancellationToken)
    {
        if (!shopId.HasValue || leadTags.Count > 0)
        {
            return [];
        }

        var sid = shopId.Value;
        var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == sid);
        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            shopQuery = shopQuery.Where(s => s.OwnerId == oid);
        }

        var shop = await shopQuery
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.ProfileName,
                s.OwnerId,
                OwnerFullName = s.Owner.FullName,
                OwnerUserName = s.Owner.UserName,
                s.ProductOpportunitiesSyncedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (shop is null)
        {
            return [];
        }

        var savedQuery = _db.ProductOpportunitySavedKeywords.AsNoTracking()
            .Where(s => s.ShopId == sid);
        if (!string.IsNullOrEmpty(keyword))
        {
            var needle = keyword.ToLowerInvariant();
            savedQuery = savedQuery.Where(s => s.KeywordNormalized.Contains(needle));
        }

        var saved = await savedQuery
            .OrderBy(s => s.Keyword)
            .Take(200)
            .Select(s => new
            {
                s.Id,
                s.Keyword,
                s.KeywordNormalized,
                s.LeadId,
                s.PicUrl
            })
            .ToListAsync(cancellationToken);
        if (saved.Count == 0)
        {
            return [];
        }

        var keys = saved
            .Select(s => s.KeywordNormalized)
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();
        var snapshotNames = keys.Count == 0
            ? new List<string>()
            : await snapshotRows
                .Where(x => keys.Contains(x.LeadName.ToLower()))
                .Select(x => x.LeadName.ToLower())
                .Distinct()
                .ToListAsync(cancellationToken);
        var snapshotSet = snapshotNames.ToHashSet(StringComparer.Ordinal);
        var extras = ProductOpportunitySavedApply.WithoutSnapshot(
            saved,
            s => s.KeywordNormalized,
            snapshotSet);

        return extras.Select(s => new ProductOpportunityListItemDto
        {
            Id = s.Id,
            ShopId = shop.Id,
            ShopName = shop.Name,
            ShopProfileName = shop.ProfileName,
            OwnerId = shop.OwnerId,
            OwnerFullName = shop.OwnerFullName,
            OwnerUserName = shop.OwnerUserName,
            LeadId = s.LeadId ?? string.Empty,
            LeadName = s.Keyword,
            PicUrl = s.PicUrl,
            PicUrls = string.IsNullOrWhiteSpace(s.PicUrl) ? [] : [s.PicUrl!],
            IsSaved = true,
            ShopCount = 1,
            ShopSyncedAt = shop.ProductOpportunitiesSyncedAt
        }).ToList();
    }

    private IQueryable<ProductOpportunity> FilterLinkedKeywords(
        IQueryable<ProductOpportunity> rows,
        Guid? shopId,
        Guid? ownerId)
    {
        if (shopId.HasValue)
        {
            var sid = shopId.Value;
            rows = rows.Where(x => x.ShopLinks.Any(l => l.ShopId == sid));
        }

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            rows = rows.Where(x => x.ShopLinks.Any(l => l.Shop.OwnerId == oid));
        }

        if (!shopId.HasValue && !ownerId.HasValue)
        {
            rows = rows.Where(x => x.ShopLinks.Any());
        }

        return rows;
    }

    private sealed record ScopedShopInfo(
        Guid ShopId,
        string ShopName,
        string? ShopProfileName,
        Guid OwnerId,
        string? OwnerFullName,
        string? OwnerUserName,
        DateTime? ShopSyncedAt,
        int ShopCount,
        IReadOnlyList<Guid> LinkedShopIds);

    private async Task<Dictionary<Guid, ScopedShopInfo>> LoadScopedShopInfoAsync(
        IReadOnlyList<Guid> opportunityIds,
        Guid? shopId,
        Guid? ownerId,
        CancellationToken cancellationToken)
    {
        if (opportunityIds.Count == 0)
        {
            return [];
        }

        var links = _db.ProductOpportunityShopLinks.AsNoTracking()
            .Where(l => opportunityIds.Contains(l.ProductOpportunityId));
        if (shopId.HasValue)
        {
            var sid = shopId.Value;
            links = links.Where(l => l.ShopId == sid);
        }

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            links = links.Where(l => l.Shop.OwnerId == oid);
        }

        var rows = await links
            .Select(l => new
            {
                l.ProductOpportunityId,
                l.ShopId,
                ShopName = l.Shop.Name,
                ShopProfileName = l.Shop.ProfileName,
                OwnerId = l.Shop.OwnerId,
                OwnerFullName = l.Shop.Owner.FullName,
                OwnerUserName = l.Shop.Owner.UserName,
                ShopSyncedAt = l.Shop.ProductOpportunitiesSyncedAt
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<Guid, ScopedShopInfo>();
        foreach (var group in rows.GroupBy(x => x.ProductOpportunityId))
        {
            var best = group
                .OrderByDescending(x => x.ShopSyncedAt)
                .ThenBy(x => x.ShopName)
                .First();
            map[group.Key] = new ScopedShopInfo(
                best.ShopId,
                best.ShopName,
                best.ShopProfileName,
                best.OwnerId,
                best.OwnerFullName,
                best.OwnerUserName,
                best.ShopSyncedAt,
                group.Count(),
                group.Select(x => x.ShopId).Distinct().ToList());
        }

        return map;
    }

    private async Task<Dictionary<string, ProductOpportunity>> LoadKeywordsByLeadIdsAsync(
        IReadOnlyList<string> leadIds,
        CancellationToken cancellationToken)
    {
        if (leadIds.Count == 0)
        {
            return new Dictionary<string, ProductOpportunity>(StringComparer.Ordinal);
        }

        var rows = await _db.ProductOpportunities
            .Where(x => leadIds.Contains(x.LeadId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.LeadId, StringComparer.Ordinal);
    }

    private void ApplyItem(
        ProductOpportunity row,
        SyncProductOpportunityItemDto item,
        DateTime now,
        bool isNew)
    {
        var leadId = (item.LeadId ?? string.Empty).Trim();
        var picUrls = NormalizePicUrls(item.PicUrls, item.PicUrl);
        row.LeadName = Truncate(item.LeadName?.Trim() ?? leadId, 500);
        row.PicUrl = picUrls.Count > 0 ? picUrls[0] : null;
        row.PicUrls = SerializeStringList(picUrls);
        row.SearchVolume = Math.Max(0, item.SearchVolume);
        row.OnlineProducts = item.OnlineProducts is null
            ? null
            : Math.Max(0, item.OnlineProducts.Value);
        row.OnlineProductsRaw = TruncateOptional(NormalizeOptional(item.OnlineProductsRaw), 32);
        row.SearchVolumeRingRatio = TruncateOptional(NormalizeOptional(item.SearchVolumeRingRatio), 32);
        row.OnlineProductsRingRatio = TruncateOptional(NormalizeOptional(item.OnlineProductsRingRatio), 32);
        row.OrderCount = item.OrderCount is null ? null : Math.Max(0, item.OrderCount.Value);
        row.OrderRaw = TruncateOptional(NormalizeOptional(item.OrderRaw), 32);
        row.OrderRingRatio = TruncateOptional(NormalizeOptional(item.OrderRingRatio), 32);
        row.Gmv = item.Gmv is null ? null : Math.Max(0, item.Gmv.Value);
        row.GmvRaw = TruncateOptional(NormalizeOptional(item.GmvRaw), 32);
        row.GmvRingRatio = TruncateOptional(NormalizeOptional(item.GmvRingRatio), 32);
        row.Categories = SerializeCategories(item.Categories);
        row.Level1CateId = TruncateOptional(NormalizeOptional(item.Level1CateId), 32);
        row.Level1CateName = TruncateOptional(NormalizeOptional(item.Level1CateName), 200);
        row.Level2CateId = TruncateOptional(NormalizeOptional(item.Level2CateId), 32);
        row.Level2CateName = TruncateOptional(NormalizeOptional(item.Level2CateName), 200);
        row.Level3CateId = TruncateOptional(NormalizeOptional(item.Level3CateId), 32);
        row.Level3CateName = TruncateOptional(NormalizeOptional(item.Level3CateName), 200);
        row.TagCodes = SerializeTagCodes(item.TagCodes);
        row.LeadTagTexts = SerializeStringList(NormalizeStringList(item.LeadTagTexts, 64, MaxLeadTagTexts));
        row.LeadSourceTag = TruncateOptional(NormalizeOptional(item.LeadSourceTag), 64);
        row.LeadClusterSourceTag = TruncateOptional(NormalizeOptional(item.LeadClusterSourceTag), 64);
        row.OpportunityType = item.OpportunityType;
        if (!isNew)
        {
            row.UpdatedAt = now;
        }
    }

    private async Task FlushKeywordWritesAsync(
        IReadOnlyList<string> payloadLeadIds,
        Dictionary<string, SyncProductOpportunityItemDto> payloadByLeadId,
        Dictionary<string, ProductOpportunity> byLeadId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (IsLeadIdConflict(ex))
            {
                foreach (var entry in _db.ChangeTracker.Entries<ProductOpportunity>()
                    .Where(e => e.State == EntityState.Added)
                    .ToList())
                {
                    entry.State = EntityState.Detached;
                }

                var reloaded = await LoadKeywordsByLeadIdsAsync(payloadLeadIds, cancellationToken);
                foreach (var leadId in payloadLeadIds)
                {
                    if (reloaded.TryGetValue(leadId, out var existing))
                    {
                        ApplyItem(existing, payloadByLeadId[leadId], now, isNew: false);
                        byLeadId[leadId] = existing;
                        continue;
                    }

                    if (!byLeadId.TryGetValue(leadId, out var draft))
                    {
                        draft = new ProductOpportunity
                        {
                            LeadId = Truncate(leadId, 64),
                            CreatedAt = now
                        };
                    }

                    ApplyItem(draft, payloadByLeadId[leadId], now, isNew: true);
                    await _db.ProductOpportunities.AddAsync(draft, cancellationToken);
                    byLeadId[leadId] = draft;
                }
            }
        }

        throw new BusinessException("Không ghi được keyword — trùng lead_id từ shop khác đang sync.");
    }

    private static bool IsLeadIdConflict(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("IX_ProductOpportunities_LeadId", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505", StringComparison.Ordinal);
    }

    private async Task<BulkDeleteProductOpportunitiesResultDto> UnlinkKeywordsAsync(
        IReadOnlyList<Guid> opportunityIds,
        Guid? ownerId,
        Guid? shopId,
        CancellationToken cancellationToken)
    {
        if (shopId.HasValue)
        {
            var sid = shopId.Value;
            var owned = ownerId.HasValue
                ? await _db.Shops.AsNoTracking().AnyAsync(
                    s => s.Id == sid && s.OwnerId == ownerId.Value,
                    cancellationToken)
                : await _db.Shops.AsNoTracking().AnyAsync(s => s.Id == sid, cancellationToken);
            if (!owned)
            {
                throw new NotFoundException("Không tìm thấy shop.");
            }
        }

        var links = _db.ProductOpportunityShopLinks
            .Include(l => l.Shop)
            .Include(l => l.ProductOpportunity)
            .Where(l => opportunityIds.Contains(l.ProductOpportunityId));
        if (shopId.HasValue)
        {
            var sid = shopId.Value;
            links = links.Where(l => l.ShopId == sid);
        }

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            links = links.Where(l => l.Shop.OwnerId == oid);
        }

        var rows = await links.ToListAsync(cancellationToken);
        var leftoverIds = opportunityIds.Except(rows.Select(l => l.ProductOpportunityId)).ToList();
        var savedOnlyQuery = _db.ProductOpportunitySavedKeywords.Where(s => leftoverIds.Contains(s.Id));
        if (shopId.HasValue)
        {
            var sid = shopId.Value;
            savedOnlyQuery = savedOnlyQuery.Where(s => s.ShopId == sid);
        }

        if (ownerId.HasValue)
        {
            var oid = ownerId.Value;
            savedOnlyQuery = savedOnlyQuery.Where(s => s.OwnerId == oid);
        }

        var savedOnly = leftoverIds.Count == 0
            ? []
            : await savedOnlyQuery.ToListAsync(cancellationToken);
        if (rows.Count == 0 && savedOnly.Count == 0)
        {
            throw new BusinessException("Không có keyword nào để xóa.");
        }

        var unlinked = rows
            .Select(l => (l.ShopId, l.Shop.OwnerId, Key: NormalizeKeyword(l.ProductOpportunity.LeadName)))
            .Concat(savedOnly.Select(s => (s.ShopId, s.OwnerId, Key: s.KeywordNormalized)))
            .Where(x => x.Key.Length > 0)
            .ToList();
        var removedLinkIds = rows.Select(l => l.Id).ToHashSet();
        var unlinkedOpportunityIds = rows.Select(l => l.ProductOpportunityId).Distinct().ToList();

        _db.ProductOpportunityShopLinks.RemoveRange(rows);
        await CleanupKeywordSideEffectsAsync(unlinked, removedLinkIds, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await PruneOrphanKeywordsAsync(unlinkedOpportunityIds, cancellationToken);
        return new BulkDeleteProductOpportunitiesResultDto
        {
            Deleted = unlinkedOpportunityIds.Count + savedOnly.Count
        };
    }

    private async Task PruneOrphanKeywordsAsync(
        IReadOnlyCollection<Guid> candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Count == 0)
        {
            return;
        }

        var stillLinked = (await _db.ProductOpportunityShopLinks
                .AsNoTracking()
                .Where(l => candidateIds.Contains(l.ProductOpportunityId))
                .Select(l => l.ProductOpportunityId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var orphans = ProductOpportunitySyncApply.OrphansAfterUnlink(candidateIds, stillLinked);
        if (orphans.Count == 0)
        {
            return;
        }

        var orphanRows = await _db.ProductOpportunities
            .Where(x => orphans.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (orphanRows.Count == 0)
        {
            return;
        }

        _db.ProductOpportunities.RemoveRange(orphanRows);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Shop?> ResolveBookmarkShopAsync(
        Guid opportunityId,
        Guid? shopId,
        Guid? ownerScope,
        CancellationToken cancellationToken)
    {
        if (shopId.HasValue)
        {
            var sid = shopId.Value;
            var shopQuery = _db.Shops.AsNoTracking().Where(s => s.Id == sid);
            if (ownerScope.HasValue)
            {
                var oid = ownerScope.Value;
                shopQuery = shopQuery.Where(s => s.OwnerId == oid);
            }

            var shop = await shopQuery.FirstOrDefaultAsync(cancellationToken);
            if (shop is null)
            {
                return null;
            }

            var linked = await _db.ProductOpportunityShopLinks.AsNoTracking()
                .AnyAsync(
                    l => l.ProductOpportunityId == opportunityId && l.ShopId == sid,
                    cancellationToken);
            return linked ? shop : null;
        }

        return await ResolveRepresentativeShopAsync(opportunityId, ownerScope, cancellationToken);
    }

    private async Task<Shop?> ResolveRepresentativeShopAsync(
        Guid opportunityId,
        Guid? ownerScope,
        CancellationToken cancellationToken)
    {
        var links = _db.ProductOpportunityShopLinks.AsNoTracking()
            .Where(l => l.ProductOpportunityId == opportunityId);
        if (ownerScope.HasValue)
        {
            var oid = ownerScope.Value;
            links = links.Where(l => l.Shop.OwnerId == oid);
        }

        return await links
            .OrderByDescending(l => l.Shop.ProductOpportunitiesSyncedAt)
            .Select(l => l.Shop)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Shop> ResolveOwnedShopAsync(
        Guid ownerId,
        string externalShopId,
        CancellationToken cancellationToken)
    {
        var id = (externalShopId ?? string.Empty).Trim();
        if (id.Length == 0)
        {
            throw new BusinessException("externalShopId / sellerId không được để trống.");
        }

        var shop = await _db.Shops
            .FirstOrDefaultAsync(
                s => s.OwnerId == ownerId && s.ExternalShopId == id,
                cancellationToken);

        if (shop is not null)
        {
            return shop;
        }

        var other = await (
            from s in _db.Shops.AsNoTracking()
            join u in _db.Users.AsNoTracking() on s.OwnerId equals u.Id into owners
            from u in owners.DefaultIfEmpty()
            where s.ExternalShopId == id
            select new
            {
                s.Name,
                OwnerUserName = u != null ? u.UserName : null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (other is not null)
        {
            var ownerLabel = string.IsNullOrWhiteSpace(other.OwnerUserName)
                ? "(unknown)"
                : other.OwnerUserName;
            throw new NotFoundException(
                $"Shop «{other.Name}» (externalShopId «{id}») thuộc seller «{ownerLabel}» — " +
                "đăng nhập đúng tài khoản owner trên extension rồi sync lại.");
        }

        throw new NotFoundException(
            $"Chưa có shop với externalShopId «{id}» — tạo/gán shop trên admin trước.");
    }

    private sealed record ResolvedCategoryInfos(
        List<List<ProductOpportunityCategoryInfoDto>> Groups,
        List<string> CatalogNames);

    private async Task<ResolvedCategoryInfos> ResolveCategoryInfosAsync(
        Shop shop,
        CancellationToken cancellationToken)
    {
        var catalog = ReadCatalogCategories(shop.CatalogCategories, shop.CategoryId, shop.CategoryName);
        if (catalog.Count == 0)
        {
            return new ResolvedCategoryInfos([], []);
        }

        var site = ResolveRegion(shop.Region);
        var catalogIds = catalog
            .Select(c => c.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var selected = await _db.TikTokCategories
            .AsNoTracking()
            .Where(c =>
                c.IsActive
                && c.Site.ToUpper() == site
                && catalogIds.Contains(c.ExternalId))
            .Select(c => new { c.ExternalId, c.Level })
            .ToListAsync(cancellationToken);

        var byExternalId = selected
            .GroupBy(c => c.ExternalId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new ProductOpportunityCategoryInfos.Node(g.First().ExternalId, g.First().Level),
                StringComparer.Ordinal);

        var groups = ProductOpportunityCategoryInfos.BuildGroups(
            catalog.Select(c => c.Id).ToList(),
            byExternalId);
        var resolvedIds = new HashSet<string>(
            groups
                .Where(g => g.Count > 0)
                .Select(g => g[^1].CateId.ToString(CultureInfo.InvariantCulture)),
            StringComparer.Ordinal);
        var names = catalog
            .Where(c => resolvedIds.Contains(c.Id.Trim()))
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ResolvedCategoryInfos(groups, names);
    }

    private static List<ShopCatalogCategoryDto> ReadCatalogCategories(
        string? json,
        string? categoryId,
        string? categoryName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                var parsed = JsonSerializer.Deserialize<List<ShopCatalogCategoryDto>>(json, JsonOptions)
                    ?? [];
                var rows = parsed
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id))
                    .GroupBy(c => c.Id.Trim(), StringComparer.Ordinal)
                    .Select(g =>
                    {
                        var first = g.First();
                        first.Id = first.Id.Trim();
                        first.Name = string.IsNullOrWhiteSpace(first.Name) ? first.Id : first.Name.Trim();
                        return first;
                    })
                    .ToList();
                if (rows.Count > 0)
                {
                    return rows;
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to legacy CategoryId.
        }

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            var id = categoryId.Trim();
            return
            [
                new ShopCatalogCategoryDto
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(categoryName) ? id : categoryName.Trim()
                }
            ];
        }

        return [];
    }

    private static string ResolveRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ? "US" : region.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? TruncateOptional(string? value, int max) =>
        value is null ? null : Truncate(value, max);

    private const int MaxPicUrls = 8;
    private const int MaxCategories = 20;
    private const int MaxLeadTagTexts = 8;

    private static string? SerializeTagCodes(List<string>? tags) =>
        SerializeStringList(NormalizeStringList(tags, 64, int.MaxValue));

    private static List<string> NormalizePicUrls(List<string>? urls, string? firstUrl)
    {
        var combined = new List<string>();
        if (urls is { Count: > 0 })
        {
            combined.AddRange(urls);
        }

        if (!string.IsNullOrWhiteSpace(firstUrl))
        {
            combined.Insert(0, firstUrl);
        }

        return NormalizeStringList(combined, 2000, MaxPicUrls);
    }

    private static List<string> NormalizeStringList(List<string>? values, int maxLen, int maxCount)
    {
        if (values is null || values.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<string>();
        foreach (var value in values)
        {
            var t = NormalizeOptional(value);
            if (t is null)
            {
                continue;
            }

            t = Truncate(t, maxLen);
            if (!seen.Add(t))
            {
                continue;
            }

            cleaned.Add(t);
            if (cleaned.Count >= maxCount)
            {
                break;
            }
        }

        return cleaned;
    }

    private static List<string> NormalizeLeadTags(List<string>? leadTags, string? leadTag)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<string>();
        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || cleaned.Count >= MaxLeadTagTexts)
            {
                return;
            }

            foreach (var part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim();
                if (t.Length is 0 or > 64 || !seen.Add(t))
                {
                    continue;
                }

                cleaned.Add(t);
                if (cleaned.Count >= MaxLeadTagTexts)
                {
                    return;
                }
            }
        }

        if (leadTags is { Count: > 0 })
        {
            foreach (var value in leadTags)
            {
                Add(value);
            }
        }

        Add(leadTag);
        return cleaned;
    }

    /// <summary>jsonb <c>@&gt;</c> (contains array), not LIKE — <c>LeadTagTexts</c> is jsonb.</summary>
    private IQueryable<ProductOpportunity> FilterByLeadTags(
        IQueryable<ProductOpportunity> rows,
        IReadOnlyList<string> tags)
    {
        IQueryable<Guid>? matchedIds = null;
        foreach (var tag in tags)
        {
            var json = JsonSerializer.Serialize(new[] { tag });
            var part = _db.ProductOpportunities
                .FromSqlInterpolated(
                    $"""
                    SELECT * FROM "ProductOpportunities" AS p
                    WHERE p."LeadTagTexts" @> CAST({json} AS jsonb)
                    """)
                .Select(x => x.Id);
            matchedIds = matchedIds is null ? part : matchedIds.Union(part);
        }

        return rows.Where(x => matchedIds!.Contains(x.Id));
    }

    private async Task<List<string>> LoadLeadTagOptionsAsync(
        IQueryable<ProductOpportunity> rows,
        CancellationToken cancellationToken)
    {
        var blobs = await rows
            .Where(x => x.LeadTagTexts != null)
            .Select(x => x.LeadTagTexts)
            .Distinct()
            .ToListAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new List<string>();
        foreach (var blob in blobs)
        {
            foreach (var tag in DeserializeStringList(blob))
            {
                if (!seen.Add(tag))
                {
                    continue;
                }

                options.Add(tag);
            }
        }

        options.Sort(StringComparer.OrdinalIgnoreCase);
        return options;
    }

    private static string? SerializeStringList(List<string> values) =>
        values.Count == 0 ? null : JsonSerializer.Serialize(values, JsonOptions);

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? SerializeCategories(List<ProductOpportunityCategoryDto>? categories)
    {
        if (categories is null || categories.Count == 0)
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<ProductOpportunityCategoryDto>();
        foreach (var cat in categories)
        {
            var cateId = TruncateOptional(NormalizeOptional(cat.CateId), 32) ?? string.Empty;
            var cateName = TruncateOptional(NormalizeOptional(cat.CateName), 200) ?? string.Empty;
            if (cateId.Length == 0 && cateName.Length == 0)
            {
                continue;
            }

            var key = cateId.Length > 0 ? cateId : cateName;
            if (!seen.Add(key))
            {
                continue;
            }

            cleaned.Add(new ProductOpportunityCategoryDto
            {
                CateId = cateId,
                CateName = cateName,
                Level = cat.Level is > 0 ? cat.Level : null
            });
            if (cleaned.Count >= MaxCategories)
            {
                break;
            }
        }

        return cleaned.Count == 0 ? null : JsonSerializer.Serialize(cleaned, JsonOptions);
    }

    private static List<ProductOpportunityCategoryDto> DeserializeCategories(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ProductOpportunityCategoryDto>>(json, JsonOptions)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
