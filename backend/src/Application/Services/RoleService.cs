using Application.DTOs.Role;
using Application.Interfaces;
using Domain.Entities;
using Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Role and permission management service implementation.
/// </summary>
public class RoleService : IRoleService
{
    private readonly IApplicationDbContext _context;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>
    /// Initializes the role service.
    /// </summary>
    public RoleService(
        IApplicationDbContext context,
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _roleManager = roleManager;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<List<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleManager.Roles
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        var permissionCounts = await _context.RolePermissions
            .GroupBy(rp => rp.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        return roles.Select(role => MapToDto(role, permissionCounts.GetValueOrDefault(role.Id))).ToList();
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindRoleAsync(roleId, cancellationToken);
        var permissionIds = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);

        return MapToDetailDto(role, permissionIds);
    }

    /// <inheritdoc />
    public async Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken cancellationToken = default)
    {
        if (RoleNames.All.Contains(dto.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessException("Tên vai trò trùng với vai trò hệ thống.");
        }

        if (await _roleManager.RoleExistsAsync(dto.Name))
        {
            throw new BusinessException("Vai trò đã tồn tại.");
        }

        var role = new ApplicationRole
        {
            Name = dto.Name,
            NormalizedName = dto.Name.ToUpperInvariant(),
            Description = dto.Description
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            throw new BusinessException(result.Errors.FirstOrDefault()?.Description ?? "Tạo vai trò thất bại.");
        }

        return MapToDto(role, 0);
    }

    /// <inheritdoc />
    public async Task<RoleDto> UpdateAsync(Guid roleId, UpdateRoleDto dto, CancellationToken cancellationToken = default)
    {
        var role = await FindRoleAsync(roleId, cancellationToken);
        var isSystem = IsSystemRole(role.Name);

        if (!isSystem)
        {
            if (RoleNames.All.Contains(dto.Name, StringComparer.OrdinalIgnoreCase))
            {
                throw new BusinessException("Tên vai trò trùng với vai trò hệ thống.");
            }

            var existing = await _roleManager.FindByNameAsync(dto.Name);
            if (existing is not null && existing.Id != roleId)
            {
                throw new BusinessException("Vai trò đã tồn tại.");
            }

            role.Name = dto.Name;
            role.NormalizedName = dto.Name.ToUpperInvariant();
        }

        role.Description = dto.Description;
        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            throw new BusinessException(result.Errors.FirstOrDefault()?.Description ?? "Cập nhật vai trò thất bại.");
        }

        var permissionCount = await _context.RolePermissions.CountAsync(rp => rp.RoleId == roleId, cancellationToken);
        return MapToDto(role, permissionCount);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await FindRoleAsync(roleId, cancellationToken);
        if (IsSystemRole(role.Name))
        {
            throw new BusinessException("Không thể xóa vai trò hệ thống.");
        }

        var usersInRole = await _userManager.GetUsersInRoleAsync(role.Name!);
        if (usersInRole.Count > 0)
        {
            throw new BusinessException("Không thể xóa vai trò đang được gán cho người dùng.");
        }

        var mappings = await _context.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(cancellationToken);
        _context.RolePermissions.RemoveRange(mappings);

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            throw new BusinessException(result.Errors.FirstOrDefault()?.Description ?? "Xóa vai trò thất bại.");
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RoleDetailDto> SetPermissionsAsync(Guid roleId, SetRolePermissionsDto dto, CancellationToken cancellationToken = default)
    {
        var role = await FindRoleAsync(roleId, cancellationToken);

        var validPermissionIds = await _context.Permissions
            .Where(p => dto.PermissionIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (validPermissionIds.Count != dto.PermissionIds.Distinct().Count())
        {
            throw new BusinessException("Một hoặc nhiều quyền không hợp lệ.");
        }

        var existing = await _context.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(cancellationToken);

        _context.RolePermissions.RemoveRange(existing);

        foreach (var permissionId in validPermissionIds)
        {
            _context.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permissionId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(roleId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Permissions
            .AsNoTracking()
            .OrderBy(p => p.Application)
            .ThenBy(p => p.Module)
            .ThenBy(p => p.Code)
            .Select(p => new PermissionDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Module = p.Module,
                Application = p.Application,
                Description = p.Description
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<ApplicationRole> FindRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            throw new NotFoundException("Không tìm thấy vai trò.");
        }

        return role;
    }

    private static bool IsSystemRole(string? roleName) =>
        roleName is not null && RoleNames.All.Contains(roleName, StringComparer.OrdinalIgnoreCase);

    private static RoleDto MapToDto(ApplicationRole role, int permissionCount) => new()
    {
        Id = role.Id,
        Name = role.Name ?? string.Empty,
        Description = role.Description,
        IsSystem = IsSystemRole(role.Name),
        PermissionCount = permissionCount,
        CreatedAt = role.CreatedAt
    };

    private static RoleDetailDto MapToDetailDto(ApplicationRole role, List<Guid> permissionIds)
    {
        var dto = new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name ?? string.Empty,
            Description = role.Description,
            IsSystem = IsSystemRole(role.Name),
            PermissionCount = permissionIds.Count,
            CreatedAt = role.CreatedAt,
            PermissionIds = permissionIds
        };

        return dto;
    }
}
