using Application.DTOs.Category;
using Application.DTOs.FourSeller;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// TikTok category catalog sync and query implementation.
/// </summary>
public class CategoryService : ICategoryService
{
    private const int MaxListTake = 200;

    private readonly IApplicationDbContext _context;
    private readonly IFourSellerApiService _fourSellerApiService;
    private readonly ISettingProvider _settings;
    private readonly ILogger<CategoryService> _logger;

    /// <summary>
    /// Initializes the category service.
    /// </summary>
    public CategoryService(
        IApplicationDbContext context,
        IFourSellerApiService fourSellerApiService,
        ISettingProvider settings,
        ILogger<CategoryService> logger)
    {
        _context = context;
        _fourSellerApiService = fourSellerApiService;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CategorySyncResultDto> SyncFromFourSellerAsync(
        Guid preferredUserId,
        SyncCategoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        var site = string.IsNullOrWhiteSpace(request.Site) ? "US" : request.Site.Trim();
        var (cookieUserId, fourSellerShopId) = await ResolveLoggedInCookieAndShopAsync(
            preferredUserId,
            site,
            cancellationToken);

        var syncedAt = DateTime.UtcNow;
        var seenExternalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fetchedParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        var updated = 0;

        var existing = await _context.TikTokCategories
            .Where(c => c.Site == site)
            .ToListAsync(cancellationToken);
        var byExternalId = existing.ToDictionary(c => c.ExternalId, StringComparer.OrdinalIgnoreCase);

        // Sequential BFS — DbContext/SettingProvider are scoped and not thread-safe.
        var queue = new Queue<(string ParentId, string? ParentPath, int Level)>();
        queue.Enqueue(("0", null, 0));

        while (queue.Count > 0)
        {
            var (parentId, parentPath, level) = queue.Dequeue();
            if (!fetchedParents.Add(parentId))
            {
                continue;
            }

            var children = await FetchChildrenAsync(
                cookieUserId,
                site,
                fourSellerShopId,
                parentId,
                cancellationToken);
            var parentExternalId = parentId == "0" ? null : parentId;

            // Parent claimed to have children but API returned none → treat as leaf.
            if (children.Count == 0 && parentExternalId is not null &&
                byExternalId.TryGetValue(parentExternalId, out var parentEntity) &&
                !parentEntity.IsLeaf)
            {
                parentEntity.IsLeaf = true;
                parentEntity.UpdatedAt = syncedAt;
                parentEntity.LastSyncedAt = syncedAt;
            }

            foreach (var node in children)
            {
                if (string.IsNullOrWhiteSpace(node.Id))
                {
                    continue;
                }

                seenExternalIds.Add(node.Id);
                var resolvedParent = !string.IsNullOrWhiteSpace(node.ParentCategoryId)
                    ? node.ParentCategoryId
                    : parentExternalId;
                var path = !string.IsNullOrWhiteSpace(node.NodePathId)
                    ? node.NodePathId!
                    : string.IsNullOrEmpty(parentPath) ? node.Id : $"{parentPath}/{node.Id}";

                if (byExternalId.TryGetValue(node.Id, out var entity))
                {
                    entity.Name = node.Name;
                    entity.ParentExternalId = resolvedParent;
                    entity.IsLeaf = node.IsLeaf;
                    entity.Level = level;
                    entity.Path = path;
                    entity.IsActive = true;
                    entity.LastSyncedAt = syncedAt;
                    entity.RawPayload = node.RawPayload;
                    entity.UpdatedAt = syncedAt;
                    updated++;
                }
                else
                {
                    entity = new TikTokCategory
                    {
                        ExternalId = node.Id,
                        Name = node.Name,
                        ParentExternalId = resolvedParent,
                        Site = site,
                        IsLeaf = node.IsLeaf,
                        Level = level,
                        Path = path,
                        IsActive = true,
                        LastSyncedAt = syncedAt,
                        RawPayload = node.RawPayload,
                        CreatedAt = syncedAt
                    };
                    _context.TikTokCategories.Add(entity);
                    byExternalId[node.Id] = entity;
                    created++;
                }

                if (!node.IsLeaf)
                {
                    queue.Enqueue((node.Id, path, level + 1));
                }
            }
        }

        var deactivated = 0;
        foreach (var entity in existing)
        {
            if (seenExternalIds.Contains(entity.ExternalId))
            {
                continue;
            }

            if (entity.IsActive)
            {
                entity.IsActive = false;
                entity.UpdatedAt = syncedAt;
                deactivated++;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new CategorySyncResultDto
        {
            Site = site,
            FourSellerShopId = fourSellerShopId,
            SyncedCount = created + updated,
            CreatedCount = created,
            UpdatedCount = updated,
            DeactivatedCount = deactivated,
            SyncedAt = syncedAt
        };
    }

    /// <inheritdoc />
    public async Task<List<CategoryTreeNodeDto>> GetTreeAsync(
        string site,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedSite = NormalizeSite(site);
        var query = _context.TikTokCategories.AsNoTracking().Where(c => c.Site.ToUpper() == normalizedSite);
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var rows = await query
            .OrderBy(c => c.Level)
            .ThenBy(c => c.Name)
            .Select(c => new
            {
                c.ExternalId,
                c.Name,
                c.ParentExternalId,
                c.IsLeaf
            })
            .ToListAsync(cancellationToken);

        var nodeMap = rows.ToDictionary(
            r => r.ExternalId,
            r => new CategoryTreeNodeDto
            {
                Value = r.ExternalId,
                Label = r.Name,
                IsLeaf = r.IsLeaf
            },
            StringComparer.OrdinalIgnoreCase);

        var roots = new List<CategoryTreeNodeDto>();
        foreach (var row in rows)
        {
            var node = nodeMap[row.ExternalId];
            if (string.IsNullOrWhiteSpace(row.ParentExternalId) ||
                !nodeMap.TryGetValue(row.ParentExternalId, out var parent))
            {
                roots.Add(node);
            }
            else
            {
                parent.Children.Add(node);
            }
        }

        return roots;
    }

    /// <inheritdoc />
    public async Task<List<CategoryTreeNodeDto>> GetChildrenAsync(
        string site,
        string? parentExternalId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedSite = NormalizeSite(site);
        var parentId = parentExternalId?.Trim();

        var query = _context.TikTokCategories.AsNoTracking().Where(c => c.Site.ToUpper() == normalizedSite);
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        query = string.IsNullOrEmpty(parentId)
            ? query.Where(c => c.ParentExternalId == null || c.ParentExternalId == "")
            : query.Where(c => c.ParentExternalId == parentId);

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new CategoryTreeNodeDto
            {
                Value = c.ExternalId,
                Label = c.Name,
                IsLeaf = c.IsLeaf
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CategoryPathDto> GetPathAsync(
        string site,
        string externalId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedSite = NormalizeSite(site);
        var targetId = externalId.Trim();
        var result = new CategoryPathDto();
        if (string.IsNullOrEmpty(targetId))
        {
            return result;
        }

        var target = await _context.TikTokCategories
            .AsNoTracking()
            .Where(c => c.Site.ToUpper() == normalizedSite && c.ExternalId == targetId)
            .Select(c => new { c.Path })
            .FirstOrDefaultAsync(cancellationToken);
        if (target is null)
        {
            return result;
        }

        var pathIds = (target.Path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (pathIds.Count == 0)
        {
            pathIds.Add(targetId);
        }
        result.PathIds = pathIds;

        // Load siblings for every level along the path in one round-trip: roots +
        // direct children of each ancestor on the path. Deeper branches load on demand.
        var parentIds = new HashSet<string>(pathIds, StringComparer.OrdinalIgnoreCase);
        var baseQuery = _context.TikTokCategories.AsNoTracking().Where(c => c.Site.ToUpper() == normalizedSite);
        if (activeOnly)
        {
            baseQuery = baseQuery.Where(c => c.IsActive);
        }

        var levelRows = await baseQuery
            .Where(c =>
                c.ParentExternalId == null ||
                c.ParentExternalId == "" ||
                parentIds.Contains(c.ParentExternalId))
            .Select(c => new
            {
                c.ExternalId,
                c.Name,
                c.ParentExternalId,
                c.IsLeaf
            })
            .ToListAsync(cancellationToken);

        var byParent = levelRows
            .GroupBy(r => string.IsNullOrEmpty(r.ParentExternalId) ? string.Empty : r.ParentExternalId,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Name)
                    .Select(r => new CategoryTreeNodeDto
                    {
                        Value = r.ExternalId,
                        Label = r.Name,
                        IsLeaf = r.IsLeaf
                    })
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        result.Options = byParent.TryGetValue(string.Empty, out var roots) ? roots : [];

        // Wire children onto the node on the path so the cascader is pre-expanded to target.
        var flat = byParent.Values.SelectMany(x => x)
            .ToDictionary(n => n.Value, n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var id in pathIds)
        {
            if (flat.TryGetValue(id, out var node) && byParent.TryGetValue(id, out var kids))
            {
                node.Children = kids;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<List<CategoryDto>> GetListAsync(
        string site,
        string? search = null,
        bool activeOnly = true,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSite = NormalizeSite(site);
        var query = _context.TikTokCategories.AsNoTracking().Where(c => c.Site.ToUpper() == normalizedSite);
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var term = search?.Trim() ?? string.Empty;
        if (term.Length > 0)
        {
            var needle = term.ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(needle) ||
                c.ExternalId.ToLower().Contains(needle));
        }

        query = query.OrderBy(c => c.Level).ThenBy(c => c.Name);
        if (take is > 0)
        {
            query = query.Take(Math.Min(take.Value, MaxListTake));
        }

        var list = await query
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                ExternalId = c.ExternalId,
                Name = c.Name,
                ParentExternalId = c.ParentExternalId,
                Site = c.Site,
                IsLeaf = c.IsLeaf,
                Level = c.Level,
                Path = c.Path,
                IsActive = c.IsActive,
                LastSyncedAt = c.LastSyncedAt
            })
            .ToListAsync(cancellationToken);

        if (term.Length > 0 && list.Count > 0)
        {
            await FillPathLabelsAsync(list, normalizedSite, cancellationToken);
        }

        return list;
    }

    private async Task FillPathLabelsAsync(
        List<CategoryDto> list,
        string normalizedSite,
        CancellationToken cancellationToken)
    {
        var ids = list
            .SelectMany(c => (c.Path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Concat(list.Select(c => c.ExternalId))
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var names = await _context.TikTokCategories
            .AsNoTracking()
            .Where(c => c.Site.ToUpper() == normalizedSite && ids.Contains(c.ExternalId))
            .Select(c => new { c.ExternalId, c.Name })
            .ToListAsync(cancellationToken);
        var nameById = names.ToDictionary(x => x.ExternalId, x => x.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var row in list)
        {
            var parts = (row.Path ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 && !string.IsNullOrEmpty(row.ExternalId))
            {
                parts = [row.ExternalId];
            }

            row.PathLabel = string.Join(
                " / ",
                parts.Select(id => nameById.TryGetValue(id, out var name) ? name : id));
        }
    }

    /// <inheritdoc />
    public async Task<CategoryDto?> GetByExternalIdAsync(
        string site,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        var normalizedSite = NormalizeSite(site);
        var id = externalId.Trim();
        var entity = await _context.TikTokCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Site.ToUpper() == normalizedSite && c.ExternalId == id,
                cancellationToken);

        return entity is null ? null : MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<RecommendCategoryResultDto> RecommendCategoryAsync(
        Guid actingUserId,
        RecommendCategoryRequest request,
        CancellationToken cancellationToken = default,
        bool allowOwnerOverride = false)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new BusinessException("Nhập Product name trước khi AI gợi ý category.");
        }

        var site = NormalizeSite(request.Site);
        var (cookieUserId, shopId) = await ResolveRecommendCookieAndShopAsync(
            actingUserId,
            site,
            request,
            allowOwnerOverride,
            cancellationToken);
        var title = request.Title.Trim();
        _logger.LogInformation(
            "4Seller AI category request: site={Site} shopId={ShopId} cookieUser={CookieUserId} title={Title}",
            site,
            shopId,
            cookieUserId,
            title);

        var ai = await _fourSellerApiService.GenerateCategoryAsync(
            cookieUserId,
            new FourSellerAiCategoryRequest
            {
                ShopId = shopId,
                Site = site,
                Title = title
            },
            cancellationToken);

        _logger.LogInformation(
            "4Seller AI category response: site={Site} shopId={ShopId} categoryId={CategoryId} name={CategoryName} nodePath={NodePath} nodePathId={NodePathId} leaf={IsLeaf}",
            site,
            shopId,
            ai.CategoryId,
            ai.CategoryName,
            ai.NodePath,
            ai.NodePathId,
            ai.IsLeaf);

        var result = new RecommendCategoryResultDto
        {
            CategoryId = ai.CategoryId,
            CategoryName = ai.CategoryName,
            NodePath = ai.NodePath,
            PathIds = ai.PathIds,
            IsLeaf = ai.IsLeaf,
            Site = site
        };

        await TryRemapBlockedCategoryAsync(result, site, cancellationToken);
        await ValidateAgainstCatalogAsync(result, site, cancellationToken);
        if (!result.IsValidated)
        {
            _logger.LogWarning(
                "4Seller AI category không hợp lệ theo catalog: site={Site} categoryId={CategoryId} reason={Reason}",
                site,
                result.CategoryId,
                result.ValidationWarning);
        }

        return result;
    }

    /// <summary>
    /// When AI (or a prior remap) lands on a leaf listed in Settings <c>category_blocked_remap</c>,
    /// swaps it for the configured sibling before catalog validation. If the replacement is missing
    /// from the catalog the original answer is kept so ValidateAgainstCatalogAsync can still warn.
    /// </summary>
    private async Task TryRemapBlockedCategoryAsync(
        RecommendCategoryResultDto result,
        string site,
        CancellationToken cancellationToken)
    {
        var raw = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.CategoryBlockedRemap, cancellationToken);
        var map = ListingCategoryBlockedRemap.Parse(
            string.IsNullOrWhiteSpace(raw)
                ? ListingAutoPublishSettingKeys.DefaultCategoryBlockedRemap
                : raw);

        if (!ListingCategoryBlockedRemap.TryResolve(map, result.CategoryId, out var replacementId))
        {
            return;
        }

        var replacement = await _context.TikTokCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Site.ToUpper() == site && c.ExternalId == replacementId,
                cancellationToken);

        if (replacement is null || !replacement.IsActive || !replacement.IsLeaf)
        {
            _logger.LogWarning(
                "Category blocked remap thất bại: {BlockedId} → {ReplacementId} không có / inactive / không phải leaf (site {Site}) — giữ kết quả gốc.",
                result.CategoryId,
                replacementId,
                site);
            result.ValidationWarning =
                $"Category {result.CategoryId} thuộc blocked remap → {replacementId}, nhưng replacement không hợp lệ trong catalog.";
            return;
        }

        var fromId = result.CategoryId;
        var fromName = result.CategoryName;
        result.ReplacedFromCategoryId = fromId;
        result.ReplacedFromCategoryName = fromName;
        result.ReplacementReason =
            $"Category {fromId} ({fromName}) nằm trong blocked remap — chuyển sang {replacement.ExternalId} ({replacement.Name}).";
        result.CategoryId = replacement.ExternalId;
        result.CategoryName = replacement.Name;
        result.NodePath = null;
        result.PathIds = [];
        result.CategoryPath = null;
        result.IsLeaf = replacement.IsLeaf;

        _logger.LogWarning(
            "Category blocked remap: {FromId} ({FromName}) → {ToId} ({ToName}) site={Site}",
            fromId,
            fromName,
            result.CategoryId,
            result.CategoryName,
            site);
    }

    /// <summary>
    /// Cross-checks the AI category against the synced catalog: it must exist for the site,
    /// be active and be a leaf. Also backfills <see cref="RecommendCategoryResultDto.PathIds"/>
    /// from <see cref="TikTokCategory.Path"/> when 4Seller omits <c>nodePathId</c>.
    /// </summary>
    private async Task ValidateAgainstCatalogAsync(
        RecommendCategoryResultDto result,
        string site,
        CancellationToken cancellationToken)
    {
        var entity = await _context.TikTokCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Site.ToUpper() == site && c.ExternalId == result.CategoryId,
                cancellationToken);

        if (entity is null)
        {
            result.IsValidated = false;
            result.ValidationWarning =
                $"Category {result.CategoryId} không có trong catalog đã sync (site {site}) — chạy Đồng bộ category.";
            return;
        }

        result.CategoryPath = entity.Path;
        result.IsLeaf = entity.IsLeaf;
        if (string.IsNullOrWhiteSpace(result.CategoryName))
        {
            result.CategoryName = entity.Name;
        }

        var pathIds = ParsePathIds(entity.Path, entity.ExternalId);
        if (pathIds.Count > result.PathIds.Count)
        {
            result.PathIds = pathIds;
        }

        if (!entity.IsActive)
        {
            result.IsValidated = false;
            result.ValidationWarning = $"Category {result.CategoryId} ({entity.Name}) đã bị vô hiệu hoá trong catalog.";
            return;
        }

        if (!entity.IsLeaf)
        {
            result.IsValidated = false;
            result.ValidationWarning =
                $"Category {result.CategoryId} ({entity.Name}) không phải leaf — TikTok chỉ nhận category lá.";
            return;
        }

        result.IsValidated = true;
        result.ValidationWarning = null;
    }

    /// <summary>
    /// Splits a catalog <c>Path</c> into Cascader ids, dropping the synthetic root marker
    /// (<c>"0"</c>) and guaranteeing the leaf id is last.
    /// </summary>
    private static List<string> ParsePathIds(string? path, string externalId)
    {
        var ids = (path ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment != "0")
            .ToList();

        if (ids.Count == 0 || !string.Equals(ids[^1], externalId, StringComparison.OrdinalIgnoreCase))
        {
            ids.Add(externalId);
        }

        return ids;
    }

    private async Task<(Guid CookieUserId, long FourSellerShopId)> ResolveRecommendCookieAndShopAsync(
        Guid actingUserId,
        string site,
        RecommendCategoryRequest request,
        bool allowOwnerOverride,
        CancellationToken cancellationToken)
    {
        if (allowOwnerOverride)
        {
            if (request.OwnerUserId is Guid ownerId && ownerId != Guid.Empty)
            {
                var shopId = request.FourSellerShopId.HasValue && request.FourSellerShopId.Value > 0
                    ? request.FourSellerShopId.Value
                    : await ResolveFourSellerShopIdAsync(ownerId, site, cancellationToken);
                return (ownerId, shopId);
            }

            var (cookieUserId, resolvedShopId) = await ResolveLoggedInCookieAndShopAsync(
                actingUserId,
                site,
                cancellationToken);
            if (request.FourSellerShopId.HasValue && request.FourSellerShopId.Value > 0)
            {
                return (cookieUserId, request.FourSellerShopId.Value);
            }

            return (cookieUserId, resolvedShopId);
        }

        var sellerCookieUserId = actingUserId;
        var sellerShopId = request.FourSellerShopId.HasValue && request.FourSellerShopId.Value > 0
            ? request.FourSellerShopId.Value
            : await ResolveFourSellerShopIdAsync(sellerCookieUserId, site, cancellationToken);
        return (sellerCookieUserId, sellerShopId);
    }

    /// <summary>
    /// Picks any account with cookie status <c>logged_in</c> that has a Connected TikTok shop.
    /// Prefers <paramref name="preferredUserId"/> when that account is eligible.
    /// </summary>
    private async Task<(Guid CookieUserId, long FourSellerShopId)> ResolveLoggedInCookieAndShopAsync(
        Guid preferredUserId,
        string site,
        CancellationToken cancellationToken)
    {
        var loggedInUserIds = await _context.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie.Trim() != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        if (loggedInUserIds.Count == 0)
        {
            throw new BusinessException(
                "Không có cookie 4Seller nào đang login. Chạy Check login tại Quản lý Người dùng trước khi đồng bộ category.");
        }

        var ordered = loggedInUserIds
            .OrderByDescending(id => id == preferredUserId)
            .ToList();

        BusinessException? lastError = null;
        foreach (var cookieUserId in ordered)
        {
            try
            {
                var shopId = await ResolveFourSellerShopIdAsync(cookieUserId, site, cancellationToken);
                return (cookieUserId, shopId);
            }
            catch (BusinessException ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new BusinessException(
            "Không có shop TikTok Connected trên 4Seller cho bất kỳ cookie logged_in nào.");
    }

    private async Task<long> ResolveFourSellerShopIdAsync(
        Guid userId,
        string site,
        CancellationToken cancellationToken)
    {
        var shops = await _fourSellerApiService.GetShopListAsync(userId, cancellationToken);

        var connected = shops
            .Where(s => string.Equals(s.StatusDesc, "Connected", StringComparison.OrdinalIgnoreCase))
            .Where(s => IsTikTokPlatform(s.Platform))
            .ToList();

        if (connected.Count == 0)
        {
            throw new BusinessException(
                "Không có shop TikTok Connected trên 4Seller. Hãy kết nối shop trước khi đồng bộ category.");
        }

        var matched = connected.FirstOrDefault(s =>
            string.Equals(s.MarketplaceId, site, StringComparison.OrdinalIgnoreCase));

        var chosen = matched ?? connected[0];
        if (chosen.Id <= 0)
        {
            throw new BusinessException(
                "Shop 4Seller không có id hợp lệ để gọi get-category-list.");
        }

        return chosen.Id;
    }

    private static bool IsTikTokPlatform(string platform) =>
        platform.Replace(" ", string.Empty)
            .Contains("tiktok", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<FourSellerCategoryNodeDto>> FetchChildrenAsync(
        Guid userId,
        string site,
        long fourSellerShopId,
        string parentId,
        CancellationToken cancellationToken)
    {
        return await _fourSellerApiService.GetCategoryListAsync(
            userId,
            new FourSellerCategoryListRequest
            {
                Site = site,
                ParentId = parentId,
                ShopId = fourSellerShopId
            },
            cancellationToken);
    }

    private static string NormalizeSite(string site) =>
        string.IsNullOrWhiteSpace(site) ? "US" : site.Trim().ToUpperInvariant();

    private static CategoryDto MapToDto(TikTokCategory c) => new()
    {
        Id = c.Id,
        ExternalId = c.ExternalId,
        Name = c.Name,
        ParentExternalId = c.ParentExternalId,
        Site = c.Site,
        IsLeaf = c.IsLeaf,
        Level = c.Level,
        Path = c.Path,
        IsActive = c.IsActive,
        LastSyncedAt = c.LastSyncedAt
    };
}
