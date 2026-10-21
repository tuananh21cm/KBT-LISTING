using Application.DTOs.ShippingTemplate;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Per-user shipping template service implementation.
/// </summary>
public class ShippingTemplateService : IShippingTemplateService
{
    public static readonly string[] AllowedWeightUnits = ["lb", "kg"];
    public static readonly string[] AllowedDimensionUnits = ["inch", "cm"];

    private readonly IApplicationDbContext _context;

    /// <summary>Maps aliases (pound / kilograms) to stored weight unit.</summary>
    public static string CanonicalizeWeightUnit(string? value)
    {
        var unit = (value ?? "lb").Trim().ToLowerInvariant();
        return unit switch
        {
            "lb" or "lbs" or "pound" or "pounds" => "lb",
            "kg" or "kgs" or "kilogram" or "kilograms" => "kg",
            _ => unit
        };
    }

    /// <summary>Maps aliases (inches / centimeter) to stored dimension unit.</summary>
    public static string CanonicalizeDimensionUnit(string? value)
    {
        var unit = (value ?? "inch").Trim().ToLowerInvariant();
        return unit switch
        {
            "inch" or "inches" or "in" => "inch",
            "cm" or "centimeter" or "centimeters" or "centimetre" or "centimetres" => "cm",
            _ => unit
        };
    }

    /// <summary>Whether the weight unit (after alias mapping) is allowed.</summary>
    public static bool IsValidWeightUnit(string? value) =>
        AllowedWeightUnits.Contains(CanonicalizeWeightUnit(value));

    /// <summary>Whether the dimension unit (after alias mapping) is allowed.</summary>
    public static bool IsValidDimensionUnit(string? value) =>
        AllowedDimensionUnits.Contains(CanonicalizeDimensionUnit(value));

    /// <summary>
    /// Initializes the shipping template service.
    /// </summary>
    public ShippingTemplateService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<ShippingTemplateDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ShippingTemplates
            .AsNoTracking()
            .Where(t => t.CreatedById == userId)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .Select(t => new ShippingTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                PackageWeight = t.PackageWeight,
                PackageLength = t.PackageLength,
                PackageWidth = t.PackageWidth,
                PackageHeight = t.PackageHeight,
                WeightUnit = t.WeightUnit,
                DimensionUnit = t.DimensionUnit,
                IsDefault = t.IsDefault,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ShippingTemplateDto> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindOwnedAsync(userId, id, cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<ShippingTemplateDto> CreateAsync(Guid userId, CreateShippingTemplateDto dto, CancellationToken cancellationToken = default)
    {
        var name = dto.Name.Trim();
        await EnsureNameUniqueAsync(userId, name, excludeId: null, cancellationToken);

        if (dto.IsDefault)
        {
            await ClearOtherDefaultsAsync(userId, excludeId: null, cancellationToken);
        }

        var entity = new ShippingTemplate
        {
            CreatedById = userId,
            Name = name,
            PackageWeight = dto.PackageWeight,
            PackageLength = dto.PackageLength,
            PackageWidth = dto.PackageWidth,
            PackageHeight = dto.PackageHeight,
            WeightUnit = CanonicalizeWeightUnit(dto.WeightUnit),
            DimensionUnit = CanonicalizeDimensionUnit(dto.DimensionUnit),
            IsDefault = dto.IsDefault,
        };

        _context.ShippingTemplates.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<ShippingTemplateDto> UpdateAsync(Guid userId, Guid id, UpdateShippingTemplateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await FindOwnedAsync(userId, id, cancellationToken);
        var name = dto.Name.Trim();
        await EnsureNameUniqueAsync(userId, name, excludeId: id, cancellationToken);

        if (dto.IsDefault)
        {
            await ClearOtherDefaultsAsync(userId, excludeId: id, cancellationToken);
        }

        entity.Name = name;
        entity.PackageWeight = dto.PackageWeight;
        entity.PackageLength = dto.PackageLength;
        entity.PackageWidth = dto.PackageWidth;
        entity.PackageHeight = dto.PackageHeight;
        entity.WeightUnit = CanonicalizeWeightUnit(dto.WeightUnit);
        entity.DimensionUnit = CanonicalizeDimensionUnit(dto.DimensionUnit);
        entity.IsDefault = dto.IsDefault;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindOwnedAsync(userId, id, cancellationToken);
        _context.ShippingTemplates.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearOtherDefaultsAsync(Guid userId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var defaults = await _context.ShippingTemplates
            .Where(t => t.CreatedById == userId && t.IsDefault && (!excludeId.HasValue || t.Id != excludeId.Value))
            .ToListAsync(cancellationToken);
        foreach (var t in defaults)
        {
            t.IsDefault = false;
        }
    }

    private async Task<ShippingTemplate> FindOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.ShippingTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.CreatedById == userId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException("Không tìm thấy shipping template.");
        }

        return entity;
    }

    private async Task EnsureNameUniqueAsync(Guid userId, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await _context.ShippingTemplates.AnyAsync(
            t => t.CreatedById == userId
                 && t.Name == name
                 && (!excludeId.HasValue || t.Id != excludeId.Value),
            cancellationToken);
        if (exists)
        {
            throw new BusinessException($"Template \"{name}\" đã tồn tại.");
        }
    }

    private static ShippingTemplateDto MapToDto(ShippingTemplate entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        PackageWeight = entity.PackageWeight,
        PackageLength = entity.PackageLength,
        PackageWidth = entity.PackageWidth,
        PackageHeight = entity.PackageHeight,
        WeightUnit = entity.WeightUnit,
        DimensionUnit = entity.DimensionUnit,
        IsDefault = entity.IsDefault,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
