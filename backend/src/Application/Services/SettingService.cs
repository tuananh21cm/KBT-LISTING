using Application.DTOs.Setting;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// System settings management service implementation.
/// </summary>
public class SettingService : ISettingService
{
    private readonly IApplicationDbContext _context;
    private readonly IListingAutoPublishCancellationCoordinator _autoPublishCancellation;

    /// <summary>
    /// Initializes the setting service.
    /// </summary>
    public SettingService(
        IApplicationDbContext context,
        IListingAutoPublishCancellationCoordinator autoPublishCancellation)
    {
        _context = context;
        _autoPublishCancellation = autoPublishCancellation;
    }

    /// <inheritdoc />
    public async Task<List<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Settings
            .AsNoTracking()
            .OrderBy(s => s.Group)
            .ThenBy(s => s.Label)
            .Select(s => MapToDto(s))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SettingDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var setting = await FindSettingAsync(id, cancellationToken);
        return MapToDto(setting);
    }

    /// <inheritdoc />
    public async Task<SettingDto> CreateAsync(CreateSettingDto dto, CancellationToken cancellationToken = default)
    {
        await EnsureUniqueKeyAsync(dto.Key, null, cancellationToken);
        ValidateValueForType(dto.Type, dto.Value);
        var normalizedValue = NormalizeValue(dto.Type, dto.Value);
        normalizedValue = ClampListingAutoPublishSettingValue(dto.Key.Trim(), normalizedValue);

        var setting = new Setting
        {
            Group = dto.Group.Trim(),
            Key = dto.Key.Trim(),
            Label = dto.Label.Trim(),
            Value = normalizedValue,
            Type = dto.Type.Trim().ToLowerInvariant(),
            Description = dto.Description?.Trim()
        };

        _context.Settings.Add(setting);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(setting);
    }

    /// <inheritdoc />
    public async Task<SettingDto> UpdateAsync(Guid id, UpdateSettingDto dto, CancellationToken cancellationToken = default)
    {
        var setting = await FindSettingAsync(id, cancellationToken);
        await EnsureUniqueKeyAsync(dto.Key, id, cancellationToken);
        ValidateValueForType(dto.Type, dto.Value);
        var normalizedValue = NormalizeValue(dto.Type, dto.Value);
        normalizedValue = ClampListingAutoPublishSettingValue(dto.Key.Trim(), normalizedValue);

        setting.Group = dto.Group.Trim();
        setting.Key = dto.Key.Trim();
        setting.Label = dto.Label.Trim();
        setting.Value = normalizedValue;
        setting.Type = dto.Type.Trim().ToLowerInvariant();
        setting.Description = dto.Description?.Trim();
        setting.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        if (string.Equals(setting.Key, ListingAutoPublishSettingKeys.Enabled, StringComparison.Ordinal)
            && !string.Equals(setting.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            _autoPublishCancellation.CancelAll("system_auto_publish_disabled");
        }

        return MapToDto(setting);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var setting = await FindSettingAsync(id, cancellationToken);
        _context.Settings.Remove(setting);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Setting> FindSettingAsync(Guid id, CancellationToken cancellationToken)
    {
        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (setting is null)
        {
            throw new NotFoundException("Không tìm thấy cấu hình.");
        }

        return setting;
    }

    private async Task EnsureUniqueKeyAsync(string key, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalized = key.Trim();
        var duplicate = await _context.Settings.AnyAsync(
            s => s.Key == normalized && (!excludeId.HasValue || s.Id != excludeId.Value),
            cancellationToken);

        if (duplicate)
        {
            throw new BusinessException("Mã cấu hình (key) đã tồn tại.");
        }
    }

    private static void ValidateValueForType(string type, string value)
    {
        var normalizedType = type.Trim().ToLowerInvariant();

        if (normalizedType == SettingTypes.Number && !string.IsNullOrWhiteSpace(value) && !decimal.TryParse(value, out _))
        {
            throw new BusinessException("Giá trị phải là số hợp lệ.");
        }

        if (normalizedType == SettingTypes.Boolean && !string.IsNullOrWhiteSpace(value))
        {
            var lower = value.Trim().ToLowerInvariant();
            if (lower is not ("true" or "false" or "1" or "0"))
            {
                throw new BusinessException("Giá trị boolean phải là true hoặc false.");
            }
        }
    }

    private static string ClampListingAutoPublishSettingValue(string key, string value)
    {
        if (!string.Equals(key, ListingAutoPublishSettingKeys.DefaultMinDraftAgeMinutes, StringComparison.Ordinal))
        {
            return value;
        }

        if (!int.TryParse(value.Trim(), out var minutes))
        {
            throw new BusinessException(
                $"Thời gian chờ draft mặc định phải ≥ {ListingAutoPublishSettingKeys.MinDraftAgeMinutes} phút.");
        }

        if (minutes < ListingAutoPublishSettingKeys.MinDraftAgeMinutes)
        {
            throw new BusinessException(
                $"Thời gian chờ draft mặc định phải ≥ {ListingAutoPublishSettingKeys.MinDraftAgeMinutes} phút.");
        }

        return minutes.ToString();
    }

    private static string NormalizeValue(string type, string value)
    {
        var normalizedType = type.Trim().ToLowerInvariant();

        if (normalizedType == SettingTypes.Boolean)
        {
            var lower = value.Trim().ToLowerInvariant();
            return lower is "1" or "true" ? "true" : "false";
        }

        return value.Trim();
    }

    private static SettingDto MapToDto(Setting setting) => new()
    {
        Id = setting.Id,
        Group = setting.Group,
        Key = setting.Key,
        Label = setting.Label,
        Value = setting.Value,
        Type = setting.Type,
        Description = setting.Description,
        CreatedAt = setting.CreatedAt,
        UpdatedAt = setting.UpdatedAt
    };
}
