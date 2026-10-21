using Application.DTOs.Order;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <inheritdoc />
public class OrderIssueTagService : IOrderIssueTagService
{
    private readonly IApplicationDbContext _db;
    private readonly ITelegramNotifier _telegram;
    private readonly ILogger<OrderIssueTagService> _logger;

    /// <summary>Creates the service.</summary>
    public OrderIssueTagService(
        IApplicationDbContext db,
        ITelegramNotifier telegram,
        ILogger<OrderIssueTagService> logger)
    {
        _db = db;
        _telegram = telegram;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IssueTagDto>> ListLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.IssueTags
            .AsNoTracking()
            .OrderByDescending(t => t.IsSystem)
            .ThenBy(t => t.Label)
            .Select(t => new IssueTagDto
            {
                Id = t.Id,
                Label = t.Label,
                IsSystem = t.IsSystem,
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IssueTagDto> CreateLibraryTagAsync(
        CreateIssueTagRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var label = NormalizeLabel(request.Label);
        var existing = await FindByLabelAsync(label, cancellationToken);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var tag = new IssueTag
        {
            Label = label,
            IsSystem = false,
            CreatedByUserId = actingUserId,
            CreatedAt = DateTime.UtcNow,
        };
        _db.IssueTags.Add(tag);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(tag);
    }

    /// <inheritdoc />
    public async Task<IssueTagDto> UpdateLibraryTagAsync(
        Guid tagId,
        UpdateIssueTagRequest request,
        CancellationToken cancellationToken = default)
    {
        var tag = await _db.IssueTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhãn.");

        var label = NormalizeLabel(request.Label);
        var clash = await FindByLabelAsync(label, cancellationToken);
        if (clash is not null && clash.Id != tag.Id)
        {
            throw new BusinessException("Nhãn này đã tồn tại.");
        }

        if (string.Equals(tag.Label, label, StringComparison.Ordinal))
        {
            return ToDto(tag);
        }

        tag.Label = label;
        tag.UpdatedAt = DateTime.UtcNow;

        var affectedOrders = await _db.Orders
            .Include(o => o.IssueTags)
            .ThenInclude(x => x.IssueTag)
            .Where(o => o.IssueTags.Any(x => x.IssueTagId == tagId))
            .ToListAsync(cancellationToken);

        foreach (var order in affectedOrders)
        {
            order.Message = BuildMessage(order.IssueTags);
            order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(tag);
    }

    /// <inheritdoc />
    public async Task DeleteLibraryTagAsync(
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        var tag = await _db.IssueTags
            .FirstOrDefaultAsync(t => t.Id == tagId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy nhãn.");

        if (tag.IsSystem)
        {
            throw new BusinessException("Không thể xóa nhãn hệ thống.");
        }

        var affectedOrders = await _db.Orders
            .Include(o => o.IssueTags)
            .ThenInclude(x => x.IssueTag)
            .Where(o => o.IssueTags.Any(x => x.IssueTagId == tagId))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var order in affectedOrders)
        {
            var rows = order.IssueTags.Where(x => x.IssueTagId == tagId).ToList();
            foreach (var row in rows)
            {
                order.IssueTags.Remove(row);
                _db.OrderIssueTags.Remove(row);
            }

            ApplyStatusAndMessage(order, order.IssueTags.Count, reopenIfDone: false);
            order.UpdatedAt = now;
        }

        _db.IssueTags.Remove(tag);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrderIssueStateDto> SetOrderTagsAsync(
        Guid orderId,
        SetOrderIssueTagsRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(o => o.IssueTags)
            .ThenInclude(x => x.IssueTag)
            .Include(o => o.Shop)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        var desiredIds = (request.TagIds ?? [])
            .Distinct()
            .ToList();

        Dictionary<Guid, IssueTag> tagsById = new();
        if (desiredIds.Count > 0)
        {
            var found = await _db.IssueTags
                .Where(t => desiredIds.Contains(t.Id))
                .ToListAsync(cancellationToken);
            if (found.Count != desiredIds.Count)
            {
                throw new BusinessException("Một hoặc nhiều tag không tồn tại.");
            }

            tagsById = found.ToDictionary(t => t.Id);
        }

        var currentIds = order.IssueTags.Select(x => x.IssueTagId).ToHashSet();
        var toAdd = desiredIds.Where(id => !currentIds.Contains(id)).ToList();
        var toRemove = order.IssueTags.Where(x => !desiredIds.Contains(x.IssueTagId)).ToList();

        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            return Snapshot(order);
        }

        var now = DateTime.UtcNow;
        foreach (var row in toRemove)
        {
            order.IssueTags.Remove(row);
            _db.OrderIssueTags.Remove(row);
        }

        foreach (var id in toAdd)
        {
            order.IssueTags.Add(new OrderIssueTag
            {
                OrderId = orderId,
                IssueTagId = id,
                AssignedByUserId = actingUserId,
                AssignedAt = now,
                IssueTag = tagsById[id],
            });
        }

        ApplyStatusAndMessage(order, desiredIds.Count, reopenIfDone: toAdd.Count > 0);
        order.UpdatedAt = now;

        var notifyBodies = toAdd.Select(id => tagsById[id].Label).ToList();
        await SaveAndNotifyAsync(order, notifyBodies, cancellationToken);
        return await ReloadSnapshotAsync(orderId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrderIssueStateDto> SetOrderStatusAsync(
        Guid orderId,
        SetOrderIssueStatusRequest request,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var status = (request.Status ?? string.Empty).Trim();
        if (!string.Equals(status, OrderIssueStatuses.Processing, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status, OrderIssueStatuses.Done, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("Trạng thái phải là Processing hoặc Done.");
        }

        var order = await _db.Orders
            .Include(o => o.IssueTags)
            .ThenInclude(x => x.IssueTag)
            .Include(o => o.Shop)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        if (string.Equals(status, OrderIssueStatuses.Done, StringComparison.OrdinalIgnoreCase))
        {
            order.IssueStatus = OrderIssueStatuses.Done;
        }
        else
        {
            order.IssueStatus = OrderIssueStatuses.Processing;
        }

        order.Message = order.IssueTags.Count > 0 ? BuildMessage(order.IssueTags) : order.Message;
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _ = actingUserId;
        return Snapshot(order);
    }

    /// <inheritdoc />
    public async Task SyncFromLegacyMessageAsync(
        Guid orderId,
        string? message,
        Guid actingUserId,
        CancellationToken cancellationToken = default)
    {
        var body = message?.Trim();
        if (string.IsNullOrEmpty(body))
        {
            await SetOrderTagsAsync(
                orderId,
                new SetOrderIssueTagsRequest { TagIds = [] },
                actingUserId,
                cancellationToken);
            return;
        }

        if (body.Length > 50)
        {
            // Library label max 50; still allow denormalized Message up to 500 via truncate label.
            body = body[..50];
        }

        var tag = await FindByLabelAsync(body, cancellationToken);
        if (tag is null)
        {
            var created = await CreateLibraryTagAsync(
                new CreateIssueTagRequest { Label = body },
                actingUserId,
                cancellationToken);
            tag = await _db.IssueTags.FirstAsync(t => t.Id == created.Id, cancellationToken);
        }

        await SetOrderTagsAsync(
            orderId,
            new SetOrderIssueTagsRequest { TagIds = [tag.Id] },
            actingUserId,
            cancellationToken);
    }

    private static void ApplyStatusAndMessage(Order order, int tagCount, bool reopenIfDone)
    {
        if (tagCount == 0)
        {
            order.IssueStatus = OrderIssueStatuses.None;
            order.Message = null;
            return;
        }

        order.Message = BuildMessage(order.IssueTags);
        if (reopenIfDone
            || string.Equals(order.IssueStatus, OrderIssueStatuses.None, StringComparison.OrdinalIgnoreCase)
            || string.Equals(order.IssueStatus, OrderIssueStatuses.Done, StringComparison.OrdinalIgnoreCase))
        {
            order.IssueStatus = OrderIssueStatuses.Processing;
        }
    }

    private static string? BuildMessage(IEnumerable<OrderIssueTag> rows)
    {
        var labels = rows
            .Select(x => x.IssueTag?.Label)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (labels.Count == 0)
        {
            return null;
        }

        var joined = string.Join(", ", labels);
        return joined.Length <= 500 ? joined : joined[..500];
    }

    private async Task SaveAndNotifyAsync(
        Order order,
        IReadOnlyList<string> addedLabels,
        CancellationToken cancellationToken)
    {
        await _db.SaveChangesAsync(cancellationToken);

        if (addedLabels.Count == 0)
        {
            return;
        }

        try
        {
            await _telegram.NotifyOrderMessageAsync(
                order,
                shopNameFallback: order.Shop?.Name,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram notify failed after issue-tag update for {OrderId}", order.Id);
        }
    }

    private async Task<IssueTag?> FindByLabelAsync(string label, CancellationToken cancellationToken)
    {
        var lower = label.ToLowerInvariant();
        return await _db.IssueTags
            .FirstOrDefaultAsync(t => t.Label.ToLower() == lower, cancellationToken);
    }

    private async Task<OrderIssueStateDto> ReloadSnapshotAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.IssueTags)
            .ThenInclude(x => x.IssueTag)
            .FirstAsync(o => o.Id == orderId, cancellationToken);
        return Snapshot(order);
    }

    private static OrderIssueStateDto Snapshot(Order order) => new()
    {
        IssueStatus = order.IssueStatus,
        Message = order.Message,
        IssueTags = order.IssueTags
            .Where(x => x.IssueTag != null)
            .Select(x => ToDto(x.IssueTag))
            .OrderByDescending(t => t.IsSystem)
            .ThenBy(t => t.Label)
            .ToList(),
    };

    private static IssueTagDto ToDto(IssueTag t) => new()
    {
        Id = t.Id,
        Label = t.Label,
        IsSystem = t.IsSystem,
    };

    private static string NormalizeLabel(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new BusinessException("Nhãn tag không được để trống.");
        }

        if (trimmed.Length > 50)
        {
            throw new BusinessException("Nhãn tag tối đa 50 ký tự.");
        }

        return trimmed;
    }
}
