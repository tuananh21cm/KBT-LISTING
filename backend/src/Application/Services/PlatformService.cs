using Application.DTOs.Platform;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Platform management service implementation.
/// </summary>
public class PlatformService : IPlatformService
{
    private readonly IApplicationDbContext _context;

    /// <summary>
    /// Initializes the platform service.
    /// </summary>
    public PlatformService(IApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<List<PlatformDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var platforms = await _context.Platforms
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new PlatformDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description,
                AvatarUrl = p.AvatarUrl,
                IsActive = p.IsActive,
                ShopCount = p.Shops.Count,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return platforms;
    }

    /// <inheritdoc />
    public async Task<PlatformDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var platform = await FindPlatformAsync(id, cancellationToken);
        return MapToDto(platform);
    }

    /// <inheritdoc />
    public async Task<List<PlatformOptionDto>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Platforms
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new PlatformOptionDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                AvatarUrl = p.AvatarUrl
            })
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PlatformDto> CreateAsync(CreatePlatformDto dto, CancellationToken cancellationToken = default)
    {
        await EnsureUniqueCodeAsync(dto.Code, null, cancellationToken);

        var platform = new Platform
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            AvatarUrl = string.IsNullOrWhiteSpace(dto.AvatarUrl) ? null : dto.AvatarUrl.Trim(),
            IsActive = dto.IsActive
        };

        _context.Platforms.Add(platform);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(platform);
    }

    /// <inheritdoc />
    public async Task<PlatformDto> UpdateAsync(Guid id, UpdatePlatformDto dto, CancellationToken cancellationToken = default)
    {
        var platform = await FindPlatformAsync(id, cancellationToken);
        await EnsureUniqueCodeAsync(dto.Code, id, cancellationToken);

        platform.Code = dto.Code.Trim();
        platform.Name = dto.Name.Trim();
        platform.Description = dto.Description?.Trim();
        platform.AvatarUrl = string.IsNullOrWhiteSpace(dto.AvatarUrl) ? null : dto.AvatarUrl.Trim();
        platform.IsActive = dto.IsActive;
        platform.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(platform);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var platform = await FindPlatformAsync(id, cancellationToken);

        var hasShops = await _context.Shops.AnyAsync(s => s.PlatformId == id, cancellationToken);
        if (hasShops)
        {
            throw new BusinessException("Không thể xóa nền tảng đang có shop sử dụng.");
        }

        _context.Platforms.Remove(platform);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Platform> FindPlatformAsync(Guid id, CancellationToken cancellationToken)
    {
        var platform = await _context.Platforms.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (platform is null)
        {
            throw new NotFoundException("Không tìm thấy nền tảng.");
        }

        return platform;
    }

    private async Task EnsureUniqueCodeAsync(string code, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim();
        var duplicate = await _context.Platforms.AnyAsync(
            p => p.Code == normalized && (!excludeId.HasValue || p.Id != excludeId.Value),
            cancellationToken);

        if (duplicate)
        {
            throw new BusinessException("Mã nền tảng đã tồn tại.");
        }
    }

    private static PlatformDto MapToDto(Platform platform) => new()
    {
        Id = platform.Id,
        Code = platform.Code,
        Name = platform.Name,
        Description = platform.Description,
        AvatarUrl = platform.AvatarUrl,
        IsActive = platform.IsActive,
        ShopCount = platform.Shops?.Count ?? 0,
        CreatedAt = platform.CreatedAt,
        UpdatedAt = platform.UpdatedAt
    };
}
