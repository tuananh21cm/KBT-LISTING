using Application.DTOs.PricingTemplate;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Per-user pricing template service implementation.
/// </summary>
public class PricingTemplateService : IPricingTemplateService
{
    public static readonly string[] AllowedTargetModes = ["margin", "profit"];

    private readonly IApplicationDbContext _context;

    /// <summary>Normalizes target mode to margin | profit.</summary>
    public static string CanonicalizeTargetMode(string? value)
    {
        var mode = (value ?? "margin").Trim().ToLowerInvariant();
        return mode is "profit" ? "profit" : "margin";
    }

    /// <summary>Whether the target mode is allowed.</summary>
    public static bool IsValidTargetMode(string? value)
    {
        var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
        return AllowedTargetModes.Contains(mode);
    }

    /// <summary>
    /// Initializes the pricing template service.
    /// </summary>
    public PricingTemplateService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<PricingTemplateDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.PricingTemplates
            .AsNoTracking()
            .Where(t => t.CreatedById == userId)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .Select(t => new PricingTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                SupplierShipping = t.SupplierShipping,
                Tax = t.Tax,
                DiscountPercent = t.DiscountPercent,
                FastSalePercent = t.FastSalePercent,
                AdsFee = t.AdsFee,
                TikTokShipping = t.TikTokShipping,
                PlatformFeePercent = t.PlatformFeePercent,
                TargetMode = t.TargetMode,
                MarginPercent = t.MarginPercent,
                ProfitAmount = t.ProfitAmount,
                IsDefault = t.IsDefault,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PricingTemplateDto> GetByIdAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindOwnedAsync(userId, id, cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<PricingTemplateDto> CreateAsync(Guid userId, CreatePricingTemplateDto dto, CancellationToken cancellationToken = default)
    {
        var name = dto.Name.Trim();
        await EnsureNameUniqueAsync(userId, name, excludeId: null, cancellationToken);

        if (dto.IsDefault)
        {
            await ClearDefaultsAsync(userId, excludeId: null, cancellationToken);
        }

        var entity = new PricingTemplate
        {
            CreatedById = userId,
            Name = name,
            SupplierShipping = dto.SupplierShipping,
            Tax = dto.Tax,
            DiscountPercent = dto.DiscountPercent,
            FastSalePercent = dto.FastSalePercent,
            AdsFee = dto.AdsFee,
            TikTokShipping = dto.TikTokShipping,
            PlatformFeePercent = dto.PlatformFeePercent,
            TargetMode = CanonicalizeTargetMode(dto.TargetMode),
            MarginPercent = dto.MarginPercent,
            ProfitAmount = dto.ProfitAmount,
            IsDefault = dto.IsDefault,
        };

        _context.PricingTemplates.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task<PricingTemplateDto> UpdateAsync(Guid userId, Guid id, UpdatePricingTemplateDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await FindOwnedAsync(userId, id, cancellationToken);
        var name = dto.Name.Trim();
        await EnsureNameUniqueAsync(userId, name, excludeId: id, cancellationToken);

        if (dto.IsDefault)
        {
            await ClearDefaultsAsync(userId, excludeId: id, cancellationToken);
        }

        entity.Name = name;
        entity.SupplierShipping = dto.SupplierShipping;
        entity.Tax = dto.Tax;
        entity.DiscountPercent = dto.DiscountPercent;
        entity.FastSalePercent = dto.FastSalePercent;
        entity.AdsFee = dto.AdsFee;
        entity.TikTokShipping = dto.TikTokShipping;
        entity.PlatformFeePercent = dto.PlatformFeePercent;
        entity.TargetMode = CanonicalizeTargetMode(dto.TargetMode);
        entity.MarginPercent = dto.MarginPercent;
        entity.ProfitAmount = dto.ProfitAmount;
        entity.IsDefault = dto.IsDefault;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await FindOwnedAsync(userId, id, cancellationToken);
        _context.PricingTemplates.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearDefaultsAsync(Guid userId, Guid? excludeId, CancellationToken cancellationToken)
    {
        var defaults = await _context.PricingTemplates
            .Where(t => t.CreatedById == userId && t.IsDefault && (!excludeId.HasValue || t.Id != excludeId.Value))
            .ToListAsync(cancellationToken);
        foreach (var t in defaults)
        {
            t.IsDefault = false;
            t.UpdatedAt = DateTime.UtcNow;
        }
    }

    private async Task<PricingTemplate> FindOwnedAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await _context.PricingTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.CreatedById == userId, cancellationToken);
        if (entity is null)
        {
            throw new NotFoundException("Không tìm thấy pricing template.");
        }

        return entity;
    }

    private async Task EnsureNameUniqueAsync(Guid userId, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var exists = await _context.PricingTemplates.AnyAsync(
            t => t.CreatedById == userId
                 && t.Name == name
                 && (!excludeId.HasValue || t.Id != excludeId.Value),
            cancellationToken);
        if (exists)
        {
            throw new BusinessException($"Template \"{name}\" đã tồn tại.");
        }
    }

    private static PricingTemplateDto MapToDto(PricingTemplate entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        SupplierShipping = entity.SupplierShipping,
        Tax = entity.Tax,
        DiscountPercent = entity.DiscountPercent,
        FastSalePercent = entity.FastSalePercent,
        AdsFee = entity.AdsFee,
        TikTokShipping = entity.TikTokShipping,
        PlatformFeePercent = entity.PlatformFeePercent,
        TargetMode = entity.TargetMode,
        MarginPercent = entity.MarginPercent,
        ProfitAmount = entity.ProfitAmount,
        IsDefault = entity.IsDefault,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
