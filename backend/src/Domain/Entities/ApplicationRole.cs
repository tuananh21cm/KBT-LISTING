using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

/// <summary>
/// Platform role stored in database.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>
    /// Role description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Role-permission mappings.
    /// </summary>
    public ICollection<RolePermission> RolePermissions { get; set; } = [];
}
