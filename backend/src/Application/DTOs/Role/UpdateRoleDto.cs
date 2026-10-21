namespace Application.DTOs.Role;

/// <summary>
/// Update role request DTO.
/// </summary>
public class UpdateRoleDto
{
    /// <summary>Role name (ignored for system roles).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Role description.</summary>
    public string? Description { get; set; }
}
