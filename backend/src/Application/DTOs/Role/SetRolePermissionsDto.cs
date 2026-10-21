namespace Application.DTOs.Role;

/// <summary>
/// Set role permissions request DTO.
/// </summary>
public class SetRolePermissionsDto
{
    /// <summary>Permission identifiers to assign.</summary>
    public List<Guid> PermissionIds { get; set; } = [];
}
