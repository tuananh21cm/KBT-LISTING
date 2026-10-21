namespace Application.DTOs.Role;

/// <summary>
/// Role summary response DTO.
/// </summary>
public class RoleDto
{
    /// <summary>Role identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Role name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Role description.</summary>
    public string? Description { get; set; }

    /// <summary>Whether this is a seeded system role.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Number of assigned permissions.</summary>
    public int PermissionCount { get; set; }

    /// <summary>Created at (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}
