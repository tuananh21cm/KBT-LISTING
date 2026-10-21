namespace Application.DTOs.Role;

/// <summary>
/// Create role request DTO.
/// </summary>
public class CreateRoleDto
{
    /// <summary>Role name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Role description.</summary>
    public string? Description { get; set; }
}
