namespace Application.DTOs.Role;

/// <summary>
/// Permission response DTO.
/// </summary>
public class PermissionDto
{
    /// <summary>Permission identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Permission code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Module grouping.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Application scope.</summary>
    public string Application { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; set; }
}
