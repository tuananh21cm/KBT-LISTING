using Application.DTOs.Shop;
using Application.Interfaces;
using Application.Validators.Shop;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Ghi snapshot điểm Health Center (AHR + Shop Performance Score) lên bảng <c>Shops</c>.
/// </summary>
public class TikTokShopScoreService : ITikTokShopScoreService
{
    private readonly IApplicationDbContext _db;
    private readonly ITikTokShopSyncLogWriter _tikTokSyncLog;

    /// <summary>Creates the service.</summary>
    public TikTokShopScoreService(
        IApplicationDbContext db,
        ITikTokShopSyncLogWriter tikTokSyncLog)
    {
        _db = db;
        _tikTokSyncLog = tikTokSyncLog;
    }

    /// <inheritdoc />
    public async Task<SyncTikTokShopScoresResultDto> SyncAsync(
        Guid ownerId,
        SyncTikTokShopScoresDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = SyncTikTokShopScoresValidator.ResolveExternalShopId(dto);
        Guid? shopId = null;
        Shop? tracked = null;
        try
        {
            var shop = await ResolveOwnedShopAsync(ownerId, externalShopId, cancellationToken);
            shopId = shop.Id;
            tracked = shop;

            var result = new SyncTikTokShopScoresResultDto { ShopId = shop.Id };

            var ahrScore = ShopScoreNormalizer.NormalizeAhrScore(dto.AhrScore);
            if (ahrScore.HasValue)
            {
                shop.AhrScore = ahrScore;
                result.UpdatedFields.Add(nameof(Shop.AhrScore));
            }

            ApplyPerformanceScore(
                dto.PerformanceScore,
                v => shop.ShopPerformanceScore = v,
                nameof(Shop.ShopPerformanceScore),
                result);
            ApplyPerformanceScore(
                dto.ProductSatisfactionScore,
                v => shop.ProductSatisfactionScore = v,
                nameof(Shop.ProductSatisfactionScore),
                result);
            ApplyPerformanceScore(
                dto.FulfillmentScore,
                v => shop.FulfillmentScore = v,
                nameof(Shop.FulfillmentScore),
                result);
            ApplyPerformanceScore(
                dto.CustomerServiceScore,
                v => shop.CustomerServiceScore = v,
                nameof(Shop.CustomerServiceScore),
                result);

            if (result.UpdatedFields.Count > 0)
            {
                shop.TikTokShopScoreSyncedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }

            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shop.Id,
                TikTokShopSyncTypes.ShopScore,
                TikTokShopSyncStatuses.Completed,
                cancellationToken: cancellationToken);

            return result;
        }
        catch (Exception ex) when (ex is not BusinessException and not NotFoundException)
        {
            // Ghi log dùng chung DbContext — bỏ điểm dang dở để SaveChanges của writer không lưu nhầm.
            RollbackScores(tracked);

            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shopId,
                TikTokShopSyncTypes.ShopScore,
                TikTokShopSyncStatuses.Failed,
                Truncate(ex.Message, 500),
                cancellationToken: cancellationToken);
            throw;
        }
    }

    private void RollbackScores(Shop? shop)
    {
        if (shop is null)
        {
            return;
        }

        try
        {
            var entry = _db.Entry(shop);
            if (entry.State == EntityState.Modified)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
        catch (InvalidOperationException)
        {
            // Context đã hỏng nặng hơn — writer có try/catch riêng, không cần chặn ở đây.
        }
    }

    private static void ApplyPerformanceScore(
        decimal? value,
        Action<decimal> assign,
        string fieldName,
        SyncTikTokShopScoresResultDto result)
    {
        var normalized = ShopScoreNormalizer.NormalizePerformanceScore(value);
        if (normalized is null)
        {
            return;
        }

        assign(normalized.Value);
        result.UpdatedFields.Add(fieldName);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
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

        // Tracking query — service ghi thẳng lên entity Shop.
        var shop = await _db.Shops
            .FirstOrDefaultAsync(s => s.OwnerId == ownerId && s.ExternalShopId == id, cancellationToken);
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
                OwnerLabel = u != null ? (u.FullName ?? u.UserName ?? s.OwnerId.ToString()) : s.OwnerId.ToString()
            }).FirstOrDefaultAsync(cancellationToken);

        throw other is null
            ? new NotFoundException("Chưa có shop — tạo hoặc gán shop trên admin trước khi sync.")
            : new NotFoundException(
                $"TikTok seller id {id} đang thuộc shop của {other.OwnerLabel}. Gán shop đúng owner trên admin.");
    }
}
