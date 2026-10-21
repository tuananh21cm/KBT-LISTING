using Application.DTOs.Shop;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>Query and manual record endpoints for TikTok Shop sync logs.</summary>
public class TikTokShopSyncLogService : ITikTokShopSyncLogService
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        TikTokShopSyncStatuses.Skipped,
        TikTokShopSyncStatuses.Failed
    };

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        TikTokShopSyncTypes.Profile,
        TikTokShopSyncTypes.Payout,
        TikTokShopSyncTypes.Opportunity,
        TikTokShopSyncTypes.Analytics,
        TikTokShopSyncTypes.HealthCenter
    };

    private readonly IApplicationDbContext _db;
    private readonly ITikTokShopSyncLogWriter _writer;

    /// <summary>Creates the service.</summary>
    public TikTokShopSyncLogService(IApplicationDbContext db, ITikTokShopSyncLogWriter writer)
    {
        _db = db;
        _writer = writer;
    }

    /// <inheritdoc />
    public async Task<TikTokShopSyncLogDto> RecordAsync(
        Guid ownerId,
        RecordTikTokShopSyncLogDto dto,
        CancellationToken cancellationToken = default)
    {
        var runId = (dto.RunId ?? string.Empty).Trim();
        if (runId.Length == 0)
        {
            throw new BusinessException("Thiếu runId.");
        }

        var syncType = (dto.SyncType ?? string.Empty).Trim();
        if (!AllowedTypes.Contains(syncType))
        {
            throw new BusinessException($"syncType không hợp lệ: {syncType}");
        }

        var status = (dto.Status ?? string.Empty).Trim();
        if (!AllowedStatuses.Contains(status))
        {
            throw new BusinessException($"status phải là skipped hoặc failed.");
        }

        var externalShopId = (dto.ExternalShopId ?? string.Empty).Trim();
        Guid? shopId = null;
        if (externalShopId.Length > 0)
        {
            shopId = await _db.Shops
                .AsNoTracking()
                .Where(s => s.OwnerId == ownerId && s.ExternalShopId == externalShopId)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        await _writer.WriteAsync(
            ownerId,
            runId,
            externalShopId,
            shopId,
            syncType,
            status,
            dto.ErrorMessage,
            TikTokShopSyncSources.Manual,
            cancellationToken);

        var row = await _db.TikTokShopSyncLogs
            .AsNoTracking()
            .Where(x => x.OwnerId == ownerId && x.RunId == runId && x.SyncType == syncType)
            .OrderByDescending(x => x.SyncedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            throw new BusinessException("Không ghi được log sync.");
        }

        return Map(row);
    }

    /// <inheritdoc />
    public async Task<TikTokShopSyncLogPageDto> ListAsync(
        Guid ownerId,
        TikTokShopSyncLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize < 1 ? 50 : query.PageSize, 1, 100);
        var page = Math.Max(1, query.Page);

        var q = _db.TikTokShopSyncLogs.AsNoTracking().Where(x => x.OwnerId == ownerId);

        var externalShopId = (query.ExternalShopId ?? string.Empty).Trim();
        if (externalShopId.Length > 0)
        {
            q = q.Where(x => x.ExternalShopId == externalShopId);
        }

        var syncType = (query.SyncType ?? string.Empty).Trim();
        if (syncType.Length > 0)
        {
            q = q.Where(x => x.SyncType == syncType);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(x => x.SyncedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => Map(x))
            .ToListAsync(cancellationToken);

        return new TikTokShopSyncLogPageDto
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private static TikTokShopSyncLogDto Map(TikTokShopSyncLog row) =>
        new()
        {
            Id = row.Id,
            RunId = row.RunId,
            OwnerId = row.OwnerId,
            ShopId = row.ShopId,
            ExternalShopId = row.ExternalShopId,
            SyncType = row.SyncType,
            Status = row.Status,
            ErrorMessage = row.ErrorMessage,
            SyncedAt = row.SyncedAt,
            Source = row.Source
        };
}
