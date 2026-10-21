using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.Shop;
using Application.Interfaces;
using Application.Validators.Shop;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// TikTok Growth Center violation/warning ingest and lightweight summaries for shop-tasks board.
/// </summary>
public partial class TikTokShopHealthRecordService : ITikTokShopHealthRecordService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    private readonly IApplicationDbContext _db;
    private readonly ITikTokShopSyncLogWriter _tikTokSyncLog;

    /// <summary>Creates the service.</summary>
    public TikTokShopHealthRecordService(
        IApplicationDbContext db,
        ITikTokShopSyncLogWriter tikTokSyncLog)
    {
        _db = db;
        _tikTokSyncLog = tikTokSyncLog;
    }

    /// <inheritdoc />
    public async Task<SyncTikTokShopHealthRecordsResultDto> SyncAsync(
        Guid ownerId,
        SyncTikTokShopHealthRecordsDto dto,
        CancellationToken cancellationToken = default)
    {
        var externalShopId = SyncTikTokShopHealthRecordsValidator.ResolveExternalShopId(dto);
        Guid? shopId = null;
        try
        {
            var shop = await ResolveOwnedShopAsync(ownerId, externalShopId, cancellationToken);
            shopId = shop.Id;

            var now = DateTime.UtcNow;
            var skipped = 0;
            var byRecordId = new Dictionary<string, SyncTikTokShopHealthRecordItemDto>(StringComparer.Ordinal);
            foreach (var item in dto.Items ?? [])
            {
                var recordId = (item.RecordId ?? string.Empty).Trim();
                if (recordId.Length == 0 || !byRecordId.TryAdd(recordId, item))
                {
                    skipped++;
                }
            }

            var result = new SyncTikTokShopHealthRecordsResultDto { ShopId = shop.Id, Skipped = skipped };
            if (byRecordId.Count == 0)
            {
                await _tikTokSyncLog.WriteAsync(
                    ownerId,
                    dto.RunId,
                    externalShopId,
                    shop.Id,
                    TikTokShopSyncTypes.HealthCenter,
                    TikTokShopSyncStatuses.Completed,
                    cancellationToken: cancellationToken);
                return result;
            }

            var recordIds = byRecordId.Keys.ToList();
            var existing = await _db.TikTokShopHealthRecords
                .Where(r => r.ShopId == shop.Id && recordIds.Contains(r.RecordId))
                .ToDictionaryAsync(r => r.RecordId, StringComparer.Ordinal, cancellationToken);

            foreach (var (recordId, item) in byRecordId)
            {
                var recordType = NormalizeRecordType(item.RecordType);
                var reason = Truncate(StripHtml(item.Reason), 500) ?? string.Empty;
                var detail = Truncate(StripHtml(item.Detail), 2000) ?? string.Empty;
                var categoryType = Truncate((item.CategoryType ?? string.Empty).Trim(), 128) ?? string.Empty;
                var actionsJson = SerializeActions(item.Actions);
                var createTime = UnixToUtc(item.CreateTime);

                if (!existing.TryGetValue(recordId, out var row))
                {
                    row = new TikTokShopHealthRecord
                    {
                        ShopId = shop.Id,
                        RecordId = recordId,
                        FirstSyncedAt = now
                    };
                    _db.TikTokShopHealthRecords.Add(row);
                    result.Inserted++;
                }
                else
                {
                    result.Updated++;
                }

                row.RecordType = recordType;
                row.Reason = reason;
                row.Detail = detail;
                row.CategoryType = categoryType;
                row.ActionTitlesJson = actionsJson;
                row.RecordStatus = item.RecordStatus;
                row.CreateTime = createTime;
                row.LastSyncedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);

            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shop.Id,
                TikTokShopSyncTypes.HealthCenter,
                TikTokShopSyncStatuses.Completed,
                cancellationToken: cancellationToken);

            return result;
        }
        catch (Exception ex) when (ex is not BusinessException and not NotFoundException)
        {
            await _tikTokSyncLog.WriteAsync(
                ownerId,
                dto.RunId,
                externalShopId,
                shopId,
                TikTokShopSyncTypes.HealthCenter,
                TikTokShopSyncStatuses.Failed,
                Truncate(ex.Message, 500),
                cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, ShopHealthSummaryDto>> GetSummariesForShopsAsync(
        IReadOnlyList<Guid> shopIds,
        CancellationToken cancellationToken = default)
    {
        if (shopIds.Count == 0)
        {
            return new Dictionary<Guid, ShopHealthSummaryDto>();
        }

        var activeStatus = TikTokShopHealthRecordStatuses.Active;
        var rows = await _db.TikTokShopHealthRecords
            .AsNoTracking()
            .Where(r => shopIds.Contains(r.ShopId)
                        && (r.HandledAt != null || r.RecordStatus == activeStatus))
            .OrderByDescending(r => r.CreateTime)
            .Select(r => new HealthRow(
                r.ShopId,
                r.RecordId,
                r.RecordType,
                r.Reason,
                r.Detail,
                r.CategoryType,
                r.ActionTitlesJson,
                r.CreateTime,
                r.HandledAt,
                r.RecordStatus))
            .ToListAsync(cancellationToken);

        var result = shopIds.ToDictionary(id => id, _ => new ShopHealthSummaryDto());
        foreach (var group in rows.GroupBy(r => r.ShopId))
        {
            if (!result.TryGetValue(group.Key, out var summary))
            {
                continue;
            }

            var items = group.ToList();
            var pending = items
                .Where(r => r.HandledAt is null && r.RecordStatus == activeStatus)
                .ToList();
            var handled = items
                .Where(r => r.HandledAt is not null)
                .ToList();

            summary.ActiveViolationCount = pending.Count(r =>
                string.Equals(r.RecordType, TikTokShopHealthRecordTypes.Violation, StringComparison.OrdinalIgnoreCase));
            summary.ActiveWarningCount = pending.Count(r =>
                string.Equals(r.RecordType, TikTokShopHealthRecordTypes.Warning, StringComparison.OrdinalIgnoreCase));
            summary.HandledViolationCount = handled.Count(r =>
                string.Equals(r.RecordType, TikTokShopHealthRecordTypes.Violation, StringComparison.OrdinalIgnoreCase));
            summary.HandledWarningCount = handled.Count(r =>
                string.Equals(r.RecordType, TikTokShopHealthRecordTypes.Warning, StringComparison.OrdinalIgnoreCase));

            summary.PendingRecent = pending
                .OrderByDescending(r => r.CreateTime)
                .Select(MapBrief)
                .ToList();
            summary.HandledRecent = handled
                .OrderByDescending(r => r.HandledAt ?? r.CreateTime)
                .Select(MapBrief)
                .ToList();
        }

        return result;
    }

    /// <inheritdoc />
    public async Task MarkHandledAsync(
        Guid? ownerScope,
        MarkShopHealthRecordHandledDto dto,
        CancellationToken cancellationToken = default)
    {
        var recordId = (dto.RecordId ?? string.Empty).Trim();
        if (recordId.Length == 0)
        {
            throw new BusinessException("recordId không được để trống.");
        }

        var shop = await _db.Shops
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.ShopId, cancellationToken);
        if (shop is null)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        if (ownerScope is not null && shop.OwnerId != ownerScope.Value)
        {
            throw new NotFoundException("Không tìm thấy shop.");
        }

        var row = await _db.TikTokShopHealthRecords
            .FirstOrDefaultAsync(
                r => r.ShopId == dto.ShopId && r.RecordId == recordId,
                cancellationToken);
        if (row is null)
        {
            throw new NotFoundException("Không tìm thấy health record.");
        }

        if (row.HandledAt is not null)
        {
            return;
        }

        row.HandledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static ShopHealthRecordBriefDto MapBrief(HealthRow row) =>
        new()
        {
            RecordId = row.RecordId,
            RecordType = row.RecordType,
            Reason = row.Reason,
            Detail = row.Detail,
            CategoryType = row.CategoryType,
            Actions = DeserializeActions(row.ActionTitlesJson),
            CreateTime = row.CreateTime,
            HandledAt = row.HandledAt
        };

    private static string NormalizeRecordType(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, TikTokShopHealthRecordTypes.Warning, StringComparison.OrdinalIgnoreCase)
            ? TikTokShopHealthRecordTypes.Warning
            : TikTokShopHealthRecordTypes.Violation;
    }

    private static string SerializeActions(List<SyncHealthActionDto>? actions)
    {
        var cleaned = (actions ?? [])
            .Select(a => new StoredHealthAction
            {
                Title = (a.Title ?? string.Empty).Trim(),
                ActionStartTime = NormalizeUnix(a.ActionStartTime),
                ActionEndTime = NormalizeUnix(a.ActionEndTime)
            })
            .Where(a => a.Title.Length > 0)
            .DistinctBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        return JsonSerializer.Serialize(cleaned, JsonOptions);
    }

    private static List<HealthActionDto> DeserializeActions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<HealthActionDto>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var legacyTitle = element.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(legacyTitle))
                    {
                        result.Add(new HealthActionDto { Title = legacyTitle });
                    }

                    continue;
                }

                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = element.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()?.Trim()
                    : null;
                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                result.Add(new HealthActionDto
                {
                    Title = title,
                    ActionStartTime = ReadStoredUnix(element, "actionStartTime"),
                    ActionEndTime = ReadStoredUnix(element, "actionEndTime")
                });
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateTime? ReadStoredUnix(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueEl))
        {
            return null;
        }

        if (valueEl.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!valueEl.TryGetInt64(out var unix) || unix <= 0)
        {
            return null;
        }

        return UnixToUtc(unix);
    }

    private static long? NormalizeUnix(long? unix) => unix is > 0 ? unix : null;

    private sealed class StoredHealthAction
    {
        public string Title { get; set; } = string.Empty;

        public long? ActionStartTime { get; set; }

        public long? ActionEndTime { get; set; }
    }

    internal static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = html
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

        text = HtmlTagRegex().Replace(text, string.Empty);
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }

    private static DateTime UnixToUtc(long unixSeconds) =>
        unixSeconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime
            : DateTime.UnixEpoch;

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

        var shop = await _db.Shops
            .AsNoTracking()
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

    private sealed record HealthRow(
        Guid ShopId,
        string RecordId,
        string RecordType,
        string Reason,
        string Detail,
        string CategoryType,
        string ActionTitlesJson,
        DateTime CreateTime,
        DateTime? HandledAt,
        short RecordStatus);
}
