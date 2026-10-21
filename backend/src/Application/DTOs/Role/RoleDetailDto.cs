namespace Application.DTOs.Role;

/// <summary>
/// Role detail response DTO with permissions.
/// </summary>
public class RoleDetailDto : RoleDto
{
    /// <summary>Assigned permission identifiers.</summary>
    public List<Guid> PermissionIds { get; set; } = [];
}
