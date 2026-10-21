using Application.DTOs.ProductSource;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Product source management service implementation.
/// </summary>
public class ProductSourceService : IProductSourceService
{
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes the product source service.
    /// </summary>
    public ProductSourceService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<ProductSourceDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProductSources
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductSourceDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                LogoUrl = p.LogoUrl,
                Domains = p.Domains,
                Description = p.Description,
                IsActive = p.IsActive,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProductSourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await FindSourceAsync(id, cancellationToken);
        return MapToDto(source);
    }

    /// <inheritdoc />
    public async Task<List<ProductSourceOptionDto>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ProductSources
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new ProductSourceOptionDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                LogoUrl = p.LogoUrl,
                Domains = p.Domains
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ProductSourceDto> CreateAsync(CreateProductSourceDto dto, CancellationToken cancellationToken = default)
    {
        await EnsureUniqueCodeAsync(dto.Code, null, cancellationToken);

        var source = new ProductSource
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? null : dto.LogoUrl.Trim(),
            Domains = string.IsNullOrWhiteSpace(dto.Domains) ? null : dto.Domains.Trim(),
            Description = dto.Description?.Trim(),
            IsActive = dto.IsActive
        };

        _context.ProductSources.Add(source);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(source);
    }

    /// <inheritdoc />
    public async Task<ProductSourceDto> UpdateAsync(Guid id, UpdateProductSourceDto dto, CancellationToken cancellationToken = default)
    {
        var source = await FindSourceAsync(id, cancellationToken);
        await EnsureUniqueCodeAsync(dto.Code, id, cancellationToken);

        source.Code = dto.Code.Trim();
        source.Name = dto.Name.Trim();
        source.LogoUrl = string.IsNullOrWhiteSpace(dto.LogoUrl) ? null : dto.LogoUrl.Trim();
        source.Domains = string.IsNullOrWhiteSpace(dto.Domains) ? null : dto.Domains.Trim();
        source.Description = dto.Description?.Trim();
        source.IsActive = dto.IsActive;
        source.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(source);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await FindSourceAsync(id, cancellationToken);

        _context.ProductSources.Remove(source);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProductSource> FindSourceAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await _context.ProductSources.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException("Không tìm thấy nguồn hàng.");
        }

        return source;
    }

    private async Task EnsureUniqueCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim();
        var duplicate = await _context.ProductSources.AnyAsync(
            p => p.Code == normalized && (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);

        if (duplicate)
        {
            throw new BusinessException("Mã nguồn hàng đã tồn tại.");
        }
    }

    private static ProductSourceDto MapToDto(ProductSource source) => new()
    {
        Id = source.Id,
        Code = source.Code,
        Name = source.Name,
        LogoUrl = source.LogoUrl,
        Domains = source.Domains,
        Description = source.Description,
        IsActive = source.IsActive,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };
}
