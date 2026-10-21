using Application.DTOs.VariationSpecTemplate;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Variation spec template service implementation.
/// </summary>
public class VariationSpecTemplateService : IVariationSpecTemplateService
{
    public static readonly string[] AllowedSpecTypes = ["Color", "Size", "Number of Items"];

    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes the variation spec template service.
    /// </summary>
    public VariationSpecTemplateService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<VariationSpecTemplateDto>> GetAllAsync(string? specType = null, CancellationToken cancellationToken = default)
    {
        var query = _context.VariationSpecTemplates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(specType))
        {
            query = query.Where(p => p.SpecType == specType.Trim());
        }

        return await query
            .OrderBy(p => p.SpecType)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Value)
            .Select(p => new VariationSpecTemplateDto
            {
                Id = p.Id,
                SpecType = p.SpecType,
                Value = p.Value,
                SortOrder = p.SortOrder,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<VariationSpecTemplateDto>> GetOptionsAsync(string? specType = null, CancellationToken cancellationToken = default)
    {
        var query = _context.VariationSpecTemplates.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(specType))
        {
            query = query.Where(p => p.SpecType == specType.Trim());
        }

        return await query
            .OrderBy(p => p.SpecType)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Value)
            .Select(p => new VariationSpecTemplateDto
            {
                Id = p.Id,
                SpecType = p.SpecType,
                Value = p.Value,
                SortOrder = p.SortOrder,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<VariationSpecTemplateDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(id, cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<VariationSpecTemplateDto> CreateAsync(CreateVariationSpecTemplateDto dto, CancellationToken cancellationToken = default)
    {
        var specType = NormalizeSpecType(dto.SpecType);
        var value = dto.Value.Trim();
        await EnsureUniqueAsync(specType, value, null, cancellationToken);

        var sortOrder = dto.SortOrder;
        if (sortOrder is null)
        {
            var max = await _context.VariationSpecTemplates
                .Where(p => p.SpecType == specType)
                .Select(p => (int?)p.SortOrder)
                .MaxAsync(cancellationToken);
            sortOrder = (max ?? -1) + 1;
        }

        var entity = new VariationSpecTemplate
        {
            SpecType = specType,
            Value = value,
            SortOrder = sortOrder.Value,
            IsActive = dto.IsActive
        };

        _context.VariationSpecTemplates.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<VariationSpecTemplateDto> UpdateAsync(Guid id, UpdateVariationSpecTemplateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(id, cancellationToken);
        var value = dto.Value.Trim();
        await EnsureUniqueAsync(entity.SpecType, value, id, cancellationToken);

        entity.Value = value;
        entity.SortOrder = dto.SortOrder;
        entity.IsActive = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(id, cancellationToken);
        _context.VariationSpecTemplates.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReorderAsync(ReorderVariationSpecTemplatesDto dto, CancellationToken cancellationToken = default)
    {
        var specType = NormalizeSpecType(dto.SpecType);
        if (dto.OrderedIds.Count == 0)
        {
            throw new BusinessException("Danh sách sắp xếp không được để trống.");
        }

        var items = await _context.VariationSpecTemplates
            .Where(p => p.SpecType == specType)
            .ToListAsync(cancellationToken);

        if (items.Count != dto.OrderedIds.Count || items.Any(p => !dto.OrderedIds.Contains(p.Id)))
        {
            throw new BusinessException("Danh sách sắp xếp không khớp với template hiện có.");
        }

        for (var i = 0; i < dto.OrderedIds.Count; i++)
        {
            var item = items.First(p => p.Id == dto.OrderedIds[i]);
            item.SortOrder = i;
            item.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<VariationSpecTemplate> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.VariationSpecTemplates.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException("Không tìm thấy template variation.");
        }

        return entity;
    }

    private async Task EnsureUniqueAsync(string specType, string value, Guid? excludeId, CancellationToken cancellationToken)
    {
        var duplicate = await _context.VariationSpecTemplates.AnyAsync(
            p => p.SpecType == specType
                 && p.Value == value
                 && (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);

        if (duplicate)
        {
            throw new BusinessException($"Giá trị \"{value}\" đã tồn tại trong {specType}.");
        }
    }

    private static string NormalizeSpecType(string specType)
    {
        var normalized = specType?.Trim() ?? string.Empty;
        if (!AllowedSpecTypes.Contains(normalized))
        {
            throw new BusinessException("SpecType phải là Color, Size hoặc Number of Items.");
        }

        return normalized;
    }

    private static VariationSpecTemplateDto MapToDto(VariationSpecTemplate entity) => new()
    {
        Id = entity.Id,
        SpecType = entity.SpecType,
        Value = entity.Value,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
