using Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>Deletes old auto publish run logs per retention setting.</summary>
public sealed class ListingAutoPublishLogRetentionService : IListingAutoPublishLogRetentionService
{
    private readonly IApplicationDbContext _db;
    private readonly ISettingProvider _settings;

    public ListingAutoPublishLogRetentionService(IApplicationDbContext db, ISettingProvider settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<int> PurgeOldRunsAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _settings.GetValueAsync(ListingAutoPublishSettingKeys.LogRetentionDays, cancellationToken);
        if (!int.TryParse(raw?.Trim(), out var days) || days < 1)
        {
            days = int.Parse(ListingAutoPublishSettingKeys.DefaultLogRetentionDays);
        }

        // ExecuteDelete: một câu DELETE ... WHERE, không nạp entity nào. Bản cũ `ToList` +
        // `RemoveRange` mang cả StepsJson/ErrorMessage của từng run về bộ nhớ rồi sinh một DELETE
        // theo Id cho mỗi dòng — đúng chỗ dễ hết bộ nhớ nhất, vì lần đầu chạy sau khi bật retention
        // là lần phải xoá nhiều nhất.
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return await _db.ListingAutoPublishRuns
            .Where(r => r.StartedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
