using Application.DTOs.Role;

namespace Application.Interfaces;

/// <summary>
/// Role and permission management service contract.
/// </summary>
public interface IRoleService
{
    /// <summary>Lists all roles.</summary>
    Task<List<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets role detail with permissions.</summary>
    Task<RoleDetailDto> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom role.</summary>
    Task<RoleDto> CreateAsync(CreateRoleDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates a role.</summary>
    Task<RoleDto> UpdateAsync(Guid roleId, UpdateRoleDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom role.</summary>
    Task DeleteAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Sets permissions for a role.</summary>
    Task<RoleDetailDto> SetPermissionsAsync(Guid roleId, SetRolePermissionsDto dto, CancellationToken cancellationToken = default);

    /// <summary>Lists all permissions.</summary>
    Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
}
