using System.Collections.Concurrent;
using System.Diagnostics;
using Application.DTOs.FourSeller;
using Application.DTOs.Promotion;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Syncs 4Seller TikTok product-discount activities into <see cref="ProductDiscountActivity"/>.
/// Scope: sellers with logged-in cookie; upsert only when shop maps to a local Active shop.
/// </summary>
public sealed class ProductDiscountSyncService : IProductDiscountSyncService
{
    private const int MaxParallelSellers = 4;
    private const int PageSize = 100;
    private const string StatusOngoing = "ONGOING";

    private readonly IApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFourSellerApiService _fourSeller;
    private readonly ILogger<ProductDiscountSyncService> _logger;

    /// <summary>Initializes the sync service.</summary>
    public ProductDiscountSyncService(
        IApplicationDbContext db,
        IServiceScopeFactory scopeFactory,
        IFourSellerApiService fourSeller,
        ILogger<ProductDiscountSyncService> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _fourSeller = fourSeller;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProductDiscountListPageDto> ListAsync(
        ProductDiscountListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200);

        var q = _db.ProductDiscountActivities.AsNoTracking().AsQueryable();

        if (query.OwnerUserId.HasValue)
        {
            q = q.Where(x => x.OwnerUserId == query.OwnerUserId.Value);
        }

        if (query.ShopId.HasValue)
        {
            q = q.Where(x => x.ShopId == query.ShopId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.PromotionStatus))
        {
            var status = query.PromotionStatus.Trim();
            q = q.Where(x => x.PromotionStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(query.DiscountType))
        {
            var type = query.DiscountType.Trim();
            q = q.Where(x => x.DiscountType == type);
        }
        else
        {
            // Product Discount list: hide Flash Deal rows when no explicit type filter.
            q = q.Where(x => x.DiscountType != PromotionDiscountTypes.FlashSale);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            q = q.Where(x => x.ActivityName.Contains(search));
        }

        var total = await q.CountAsync(cancellationToken);

        var rows = await q
            .OrderByDescending(x => x.BeginTimeUtc)
            .ThenByDescending(x => x.FourSellerActivityId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.OwnerUserId,
                x.ShopId,
                x.ShopName,
                x.FourSellerActivityId,
                x.PlatformActivityId,
                x.ActivityName,
                x.DiscountType,
                x.PromotionStatus,
                x.ProductLevel,
                x.BeginTimeUtc,
                x.EndTimeUtc,
                x.ProductCount,
                x.ProductFailCount,
                x.ErrorMessage,
                x.SyncedAtUtc
            })
            .ToListAsync(cancellationToken);

        var ownerIds = rows.Select(r => r.OwnerUserId).Distinct().ToList();
        var ownerNames = await _db.Users
            .AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.UserName })
            .ToDictionaryAsync(
                u => u.Id,
                u => string.IsNullOrWhiteSpace(u.FullName) ? u.UserName : u.FullName,
                cancellationToken);

        return new ProductDiscountListPageDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            Items = rows.Select(r => new ProductDiscountListItemDto
            {
                Id = r.Id,
                OwnerUserId = r.OwnerUserId,
                OwnerName = ownerNames.GetValueOrDefault(r.OwnerUserId),
                ShopId = r.ShopId,
                ShopName = r.ShopName,
                FourSellerActivityId = r.FourSellerActivityId,
                PlatformActivityId = r.PlatformActivityId,
                ActivityName = r.ActivityName,
                DiscountType = r.DiscountType,
                PromotionStatus = r.PromotionStatus,
                ProductLevel = r.ProductLevel,
                BeginTimeUtc = r.BeginTimeUtc,
                EndTimeUtc = r.EndTimeUtc,
                ProductCount = r.ProductCount,
                ProductFailCount = r.ProductFailCount,
                ErrorMessage = r.ErrorMessage,
                SyncedAtUtc = r.SyncedAtUtc
            }).ToList()
        };
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(
        Guid activityId,
        Guid? restrictOwnerUserId = null,
        string? requiredDiscountType = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.ProductDiscountActivities
            .FirstOrDefaultAsync(x => x.Id == activityId, cancellationToken);

        if (entity is null)
        {
            throw new BusinessException("Không tìm thấy promotion activity.");
        }

        if (restrictOwnerUserId.HasValue && entity.OwnerUserId != restrictOwnerUserId.Value)
        {
            throw new BusinessException("Không tìm thấy promotion activity.");
        }

        if (!string.IsNullOrWhiteSpace(requiredDiscountType))
        {
            if (!string.Equals(
                    entity.DiscountType,
                    requiredDiscountType.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException("Không tìm thấy promotion activity.");
            }
        }
        else if (string.Equals(
                     entity.DiscountType,
                     PromotionDiscountTypes.FlashSale,
                     StringComparison.OrdinalIgnoreCase))
        {
            // Product-discount endpoints must not deactivate flash deals.
            throw new BusinessException("Không tìm thấy Product Discount activity.");
        }

        if (!string.Equals(entity.PromotionStatus, StatusOngoing, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(
                $"Chỉ deactivate được activity đang ONGOING (hiện tại: {entity.PromotionStatus}).");
        }

        if (entity.FourSellerActivityId <= 0)
        {
            throw new BusinessException("Activity thiếu FourSellerActivityId, không thể deactivate.");
        }

        await _fourSeller.DeactivateProductDiscountActivityAsync(
            entity.OwnerUserId,
            entity.FourSellerActivityId,
            entity.DiscountType,
            cancellationToken);

        var activityIdLog = entity.Id;
        var discountTypeLog = entity.DiscountType;
        var fourSellerIdLog = entity.FourSellerActivityId;
        var ownerIdLog = entity.OwnerUserId;

        _db.ProductDiscountActivities.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Deactivated and removed promotion activity {ActivityId} type {DiscountType} (4Seller id {FourSellerId}) for owner {OwnerId}",
            activityIdLog,
            discountTypeLog,
            fourSellerIdLog,
            ownerIdLog);
    }

    /// <inheritdoc />
    public async Task<ProductDiscountSyncResultDto> SyncAsync(
        ProductDiscountSyncRequest request,
        IProgress<ProductDiscountSyncSellerProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ownersQuery = _db.Users
            .AsNoTracking()
            .Where(u =>
                u.FourSellerCookie != null &&
                u.FourSellerCookie.Trim() != string.Empty &&
                u.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.LoggedIn);

        if (request.OwnerUserId.HasValue)
        {
            ownersQuery = ownersQuery.Where(u => u.Id == request.OwnerUserId.Value);
        }

        var owners = await ownersQuery
            .Select(u => new { u.Id, u.UserName, u.FullName })
            .ToListAsync(cancellationToken);

        var result = new ProductDiscountSyncResultDto();

        if (request.OwnerUserId.HasValue && owners.Count == 0)
        {
            var ownerMeta = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == request.OwnerUserId.Value)
                .Select(u => new
                {
                    HasCookie = u.FourSellerCookie != null && u.FourSellerCookie.Trim() != string.Empty,
                    u.FourSellerCookieLoginStatus
                })
                .FirstOrDefaultAsync(cancellationToken);

            var message = ownerMeta is null || !ownerMeta.HasCookie
                ? "Seller không tồn tại hoặc chưa có cookie 4Seller."
                : ownerMeta.FourSellerCookieLoginStatus == FourSellerCookieLoginStatuses.Expired
                    ? "Cookie 4Seller đã hết hạn. Chạy Check login hoặc cập nhật cookie."
                    : "Cookie 4Seller chưa được xác nhận đăng nhập. Chạy Check login trước khi đồng bộ.";

            result.FailedSellers.Add(new ProductDiscountSyncSellerFailureDto
            {
                OwnerUserId = request.OwnerUserId.Value,
                Message = message
            });
            return result;
        }

        if (owners.Count == 0)
        {
            return result;
        }

        if (progress is not null)
        {
            foreach (var owner in owners)
            {
                progress.Report(new ProductDiscountSyncSellerProgressDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = string.IsNullOrWhiteSpace(owner.FullName) ? owner.UserName : owner.FullName,
                    Status = ProductDiscountSyncSellerStatuses.Pending
                });
            }
        }

        using var sellerGate = new SemaphoreSlim(MaxParallelSellers, MaxParallelSellers);
        var tasks = owners.Select(async owner =>
        {
            var ownerName = string.IsNullOrWhiteSpace(owner.FullName) ? owner.UserName : owner.FullName;
            await sellerGate.WaitAsync(cancellationToken);
            try
            {
                progress?.Report(new ProductDiscountSyncSellerProgressDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = ownerName,
                    Status = ProductDiscountSyncSellerStatuses.Running
                });

                var sw = Stopwatch.StartNew();
                var partial = await SyncOwnerInScopeAsync(
                    owner.Id,
                    request.DiscountType?.Trim() ?? string.Empty,
                    cancellationToken);
                sw.Stop();

                progress?.Report(new ProductDiscountSyncSellerProgressDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = ownerName,
                    Status = ProductDiscountSyncSellerStatuses.Succeeded,
                    Inserted = partial.Inserted,
                    Updated = partial.Updated,
                    Skipped = partial.Skipped,
                    ElapsedMs = sw.ElapsedMilliseconds
                });

                return (Ok: true, OwnerId: owner.Id, OwnerName: ownerName, Partial: partial, Error: (string?)null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Product discount sync failed for seller {OwnerId}", owner.Id);
                progress?.Report(new ProductDiscountSyncSellerProgressDto
                {
                    OwnerUserId = owner.Id,
                    OwnerName = ownerName,
                    Status = ProductDiscountSyncSellerStatuses.Failed,
                    Message = ex.Message
                });
                return (Ok: false, OwnerId: owner.Id, OwnerName: ownerName, Partial: (SellerPartial?)null, Error: ex.Message);
            }
            finally
            {
                sellerGate.Release();
            }
        });

        var outcomes = await Task.WhenAll(tasks);
        foreach (var outcome in outcomes)
        {
            if (!outcome.Ok || outcome.Partial is null)
            {
                result.FailedSellers.Add(new ProductDiscountSyncSellerFailureDto
                {
                    OwnerUserId = outcome.OwnerId,
                    OwnerName = outcome.OwnerName,
                    Message = outcome.Error ?? "Đồng bộ thất bại."
                });
                continue;
            }

            result.SyncedSellers++;
            result.Inserted += outcome.Partial.Inserted;
            result.Updated += outcome.Partial.Updated;
            result.Skipped += outcome.Partial.Skipped;
        }

        return result;
    }

    private async Task<SellerPartial> SyncOwnerInScopeAsync(
        Guid ownerId,
        string discountType,
        CancellationToken cancellationToken)
    {
        // Separate scope so parallel sellers do not share one DbContext / sticky proxy.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var fourSeller = scope.ServiceProvider.GetRequiredService<IFourSellerApiService>();
        return await SyncOwnerAsync(db, fourSeller, ownerId, discountType, cancellationToken);
    }

    private static async Task<SellerPartial> SyncOwnerAsync(
        IApplicationDbContext db,
        IFourSellerApiService fourSeller,
        Guid ownerId,
        string discountType,
        CancellationToken cancellationToken)
    {
        var activeShops = await db.Shops
            .AsNoTracking()
            .Where(s =>
                s.OwnerId == ownerId &&
                s.Status == ShopStatuses.Active &&
                s.FourSellerShopId != null &&
                s.FourSellerShopId != "")
            .Select(s => new { s.Id, s.FourSellerShopId })
            .ToListAsync(cancellationToken);

        var shopByFourSellerId = new Dictionary<long, Guid>();
        foreach (var shop in activeShops)
        {
            if (long.TryParse(shop.FourSellerShopId, out var fsId) && fsId > 0)
            {
                shopByFourSellerId[fsId] = shop.Id;
            }
        }

        var isFlashScope = string.Equals(
            discountType,
            PromotionDiscountTypes.FlashSale,
            StringComparison.OrdinalIgnoreCase);

        if (shopByFourSellerId.Count == 0)
        {
            var deletedEmpty = await DeleteStaleInScopeAsync(
                db,
                ownerId,
                isFlashScope,
                keepFourSellerIds: null,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new SellerPartial(0, 0, deletedEmpty);
        }

        var records = new List<FourSellerProductDiscountRecordDto>();
        var pageCurrent = 1;
        int total;
        do
        {
            var page = await fourSeller.GetProductDiscountActivityPageAsync(
                ownerId,
                new FourSellerProductDiscountPageRequest
                {
                    PageCurrent = pageCurrent,
                    PageSize = PageSize,
                    DiscountType = discountType,
                    ShopId = string.Empty,
                    PromotionStatus = StatusOngoing,
                    SearchType = "activityName",
                    SearchValue = string.Empty
                },
                cancellationToken);

            total = page.Total;
            if (page.Records.Count == 0)
            {
                break;
            }

            records.AddRange(page.Records);
            pageCurrent++;
        } while (records.Count < total);

        // Product-discount sync (empty type) may still return mixed rows — drop FLASHSALE.
        // Flash sync should only keep FLASHSALE. Only keep ONGOING rows.
        if (string.IsNullOrWhiteSpace(discountType))
        {
            records = records
                .Where(r => !string.Equals(
                    r.DiscountType,
                    PromotionDiscountTypes.FlashSale,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (isFlashScope)
        {
            records = records
                .Where(r => string.Equals(
                    r.DiscountType,
                    PromotionDiscountTypes.FlashSale,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        records = records
            .Where(r => string.Equals(
                r.PromotionStatus,
                StatusOngoing,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        var activityIds = records.Select(r => r.Id).Where(id => id > 0).Distinct().ToList();
        var existing = await db.ProductDiscountActivities
            .Where(x => x.OwnerUserId == ownerId && activityIds.Contains(x.FourSellerActivityId))
            .ToDictionaryAsync(x => x.FourSellerActivityId, cancellationToken);

        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var keepIds = new HashSet<long>();

        foreach (var record in records)
        {
            if (record.Id <= 0 || record.ShopId <= 0)
            {
                skipped++;
                continue;
            }

            if (!shopByFourSellerId.TryGetValue(record.ShopId, out var localShopId))
            {
                skipped++;
                continue;
            }

            var beginUtc = FromEpochMs(record.BeginTime);
            var endUtc = FromEpochMs(record.EndTime);
            var error = string.IsNullOrWhiteSpace(record.ErrorMessage) ? null : record.ErrorMessage.Trim();

            if (existing.TryGetValue(record.Id, out var entity))
            {
                entity.ShopId = localShopId;
                entity.FourSellerShopId = record.ShopId;
                entity.ShopName = Truncate(record.ShopName, 300);
                entity.PlatformActivityId = Truncate(record.PlatformActivityId, 64);
                entity.ActivityName = Truncate(record.ActivityName, 500);
                entity.DiscountType = Truncate(record.DiscountType, 64);
                entity.PromotionStatus = StatusOngoing;
                entity.ProductLevel = Truncate(record.ProductLevel, 64);
                entity.BeginTimeUtc = beginUtc;
                entity.EndTimeUtc = endUtc;
                entity.ProductCount = record.ProductCount;
                entity.ProductFailCount = record.ProductFailCount;
                entity.ErrorMessage = TruncateNullable(error, 2000);
                entity.SyncedAtUtc = now;
                entity.UpdatedAt = now;
                updated++;
            }
            else
            {
                db.ProductDiscountActivities.Add(new ProductDiscountActivity
                {
                    OwnerUserId = ownerId,
                    ShopId = localShopId,
                    FourSellerActivityId = record.Id,
                    FourSellerShopId = record.ShopId,
                    ShopName = Truncate(record.ShopName, 300),
                    PlatformActivityId = Truncate(record.PlatformActivityId, 64),
                    ActivityName = Truncate(record.ActivityName, 500),
                    DiscountType = Truncate(record.DiscountType, 64),
                    PromotionStatus = StatusOngoing,
                    ProductLevel = Truncate(record.ProductLevel, 64),
                    BeginTimeUtc = beginUtc,
                    EndTimeUtc = endUtc,
                    ProductCount = record.ProductCount,
                    ProductFailCount = record.ProductFailCount,
                    ErrorMessage = TruncateNullable(error, 2000),
                    SyncedAtUtc = now,
                    CreatedAt = now
                });
                inserted++;
            }

            keepIds.Add(record.Id);
        }

        var deleted = await DeleteStaleInScopeAsync(
            db,
            ownerId,
            isFlashScope,
            keepIds,
            cancellationToken);
        skipped += deleted;

        await db.SaveChangesAsync(cancellationToken);
        return new SellerPartial(inserted, updated, skipped);
    }

    /// <summary>
    /// Removes local rows in the PD or Flash Deal scope that are not in the kept ONGOING set.
    /// When <paramref name="keepFourSellerIds"/> is null/empty, clears the whole scope for the owner.
    /// </summary>
    private static async Task<int> DeleteStaleInScopeAsync(
        IApplicationDbContext db,
        Guid ownerId,
        bool isFlashScope,
        HashSet<long>? keepFourSellerIds,
        CancellationToken cancellationToken)
    {
        var q = db.ProductDiscountActivities.Where(x => x.OwnerUserId == ownerId);
        if (isFlashScope)
        {
            q = q.Where(x => x.DiscountType == PromotionDiscountTypes.FlashSale);
        }
        else
        {
            q = q.Where(x => x.DiscountType != PromotionDiscountTypes.FlashSale);
        }

        if (keepFourSellerIds is { Count: > 0 })
        {
            q = q.Where(x => !keepFourSellerIds.Contains(x.FourSellerActivityId));
        }

        var stale = await q.ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        db.ProductDiscountActivities.RemoveRange(stale);
        return stale.Count;
    }

    private static DateTime FromEpochMs(long ms)
    {
        if (ms <= 0)
        {
            return DateTime.UnixEpoch;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
    }

    private static string Truncate(string? value, int max)
    {
        var s = value?.Trim() ?? string.Empty;
        return s.Length <= max ? s : s[..max];
    }

    private static string? TruncateNullable(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var s = value.Trim();
        return s.Length <= max ? s : s[..max];
    }

    private sealed record SellerPartial(int Inserted, int Updated, int Skipped);
}
